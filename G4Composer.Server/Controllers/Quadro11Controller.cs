using G4Composer.Api.Models;
using G4Composer.Server.Examples;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Swashbuckle.AspNetCore.Filters;
using System.Diagnostics;
using System.Globalization;

namespace G4Composer.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class Quadro11Controller : ControllerBase
{
    private readonly ILogger<Quadro11Controller> _logger;
    private readonly IConfiguration _config;

    private string DockerImage => _config["Quadro14g:DockerImage"] ?? "quadro14g:latest";
    private int DockerTimeoutSeconds => _config.GetValue("Quadro14g:TimeoutSeconds", 300);

    public Quadro11Controller(ILogger<Quadro11Controller> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    // ── Health ────────────────────────────────────────────────────────────────

    [HttpGet("health")]
    [SwaggerOperation(Summary = "Health check", Tags = new[] { "Quadro14g" })]
    [ProducesResponseType(typeof(HealthDto), 200)]
    public async Task<IActionResult> Health()
    {
        bool dockerAvailable = await CheckDockerAvailableAsync();
        bool imageExists = dockerAvailable && await CheckDockerImageExistsAsync(DockerImage);
        return Ok(new HealthDto
        {
            Status = imageExists ? "ready" : "degraded",
            DockerAvailable = dockerAvailable,
            ImageExists = imageExists,
            ImageName = DockerImage,
            Timestamp = DateTimeOffset.UtcNow,
        });
    }

    // ── Example ───────────────────────────────────────────────────────────────

    [HttpGet("example")]
    [SwaggerOperation(Summary = "Example input", Tags = new[] { "Quadro14g" })]
    [ProducesResponseType(typeof(List<QuadroInput>), 200)]
    [SwaggerResponseExample(200, typeof(Quadro11InputListExample))]
    public IActionResult GetExample() => Ok(Quadro11InputListExample.GetExample());

    // ── Run ───────────────────────────────────────────────────────────────────

    [HttpPost("run")]
    [Consumes("application/json")]
    [Produces("chemical/x-pdb", "application/json")]
    [SwaggerOperation(
        Summary = "Run Quadro11 computation",
        Description = "Generates .inp files, runs the quadro14g:latest Docker container and returns the resulting .pdb file.",
        Tags = new[] { "Quadro14g" }
    )]
    [SwaggerRequestExample(typeof(List<QuadroInput>), typeof(Quadro11InputListExample))]
    [ProducesResponseType(typeof(FileContentResult), 200, "chemical/x-pdb")]
    [ProducesResponseType(typeof(ErrorDto), 400)]
    [ProducesResponseType(typeof(ErrorDto), 500)]
    public async Task<IActionResult> Run([FromBody] List<QuadroInput> inputs)
    {
        if (inputs is null || inputs.Count == 0)
            return BadRequest(new ErrorDto("No input data. A list of QuadroInput is required."));

        var errors = ValidateInputs(inputs);
        if (errors.Any())
            return BadRequest(new ErrorDto("Validation error", string.Join("; ", errors)));

        string jobId = Guid.NewGuid().ToString("N")[..12];
        string jobDir = Path.Combine(Path.GetTempPath(), "g4composer_" + jobId);
        Directory.CreateDirectory(jobDir);

        _logger.LogInformation("Job {JobId}: started, {Count} structure(s)", jobId, inputs.Count);

        try
        {
            // Generate one .inp per input structure
            for (int i = 0; i < inputs.Count; i++)
            {
                string content = GenerateInpFile(inputs[i]);
                string path = Path.Combine(jobDir, $"struct_{i:D3}.inp");
                await System.IO.File.WriteAllTextAsync(path, content);
                _logger.LogDebug("Job {JobId}: wrote {File}", jobId, path);
                _logger.LogDebug("Job {JobId}: INP content [{I}]:\n{Content}", jobId, i, content);
            }

            // Run the container once per .inp file; keep the last PDB
            byte[]? pdbContent = null;
            for (int i = 0; i < inputs.Count; i++)
            {
                string inpName = $"struct_{i:D3}.inp";
                pdbContent = await RunDockerAsync(jobId, jobDir, inpName);
            }

            if (pdbContent is null || pdbContent.Length == 0)
                return StatusCode(500, new ErrorDto("Container did not produce a PDB file."));

            _logger.LogInformation("Job {JobId}: success, {Bytes} bytes", jobId, pdbContent.Length);

            Response.Headers["X-Job-Id"] = jobId;
            Response.Headers["X-Atom-Count"] = CountPdbAtoms(pdbContent).ToString();
            Response.Headers["Access-Control-Expose-Headers"] = "X-Job-Id, X-Atom-Count";

            return File(pdbContent, "chemical/x-pdb", $"g4_{jobId}.pdb");
        }
        catch (DockerException ex)
        {
            _logger.LogError(ex, "Job {JobId}: Docker error", jobId);
            return StatusCode(500, new ErrorDto($"Docker container error: {ex.Message}", ex.DockerOutput));
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Job {JobId}: timeout after {Sec}s", jobId, DockerTimeoutSeconds);
            return StatusCode(500, new ErrorDto($"Container timeout after {DockerTimeoutSeconds}s"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId}: unexpected error", jobId);
            return StatusCode(500, new ErrorDto($"Internal error: {ex.Message}"));
        }
        finally
        {
            _ = Task.Run(() => CleanupJobDir(jobDir));
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static List<string> ValidateInputs(List<QuadroInput> inputs)
    {
        var errors = new List<string>();
        for (int i = 0; i < inputs.Count; i++)
        {
            var inp = inputs[i];
            if (string.IsNullOrWhiteSpace(inp.Sequence))
                errors.Add($"[{i}] Missing sequence");
            else if (!inp.Sequence.All(c => "acgutACGUT".Contains(c)))
                errors.Add($"[{i}] Sequence '{inp.Name}' contains invalid characters (allowed: a c g u t)");

            if (inp.Twist is < 0 or > 90)
                errors.Add($"[{i}] Twist must be 0-90 (got: {inp.Twist})");

            if (inp.SugarPucker is not "N" and not "S")
                errors.Add($"[{i}] SugarPucker must be 'N' (RNA) or 'S' (DNA)");
        }
        return errors;
    }

    /// <summary>
    /// Generates a .inp file in the exact format expected by quadro14G.exe.
    ///
    /// Reference working example (6a-1hap_js12B.inp):
    ///   name        1hap_js12B_100
    ///   sequence    ggttggtgtggttgg      ← lowercase! uppercase T = T3 error
    ///   structure   AB..BA...AB..BA
    ///   chi         S...S....S...S.
    ///   orient      A+;B-
    ///   rise        3.4
    ///   twist       19                   ← integer if no fraction, dot separator
    ///   path        A1;B1;B4;A4;A3;B3;B2;A2  ← semicolon-joined list
    ///   test        y                    ← NOT "isTest"
    ///   rm_level    5                    ← NOT "RM_Level"
    ///   iteration   100                  ← NOT "Iterations"
    /// </summary>
    private static string GenerateInpFile(QuadroInput input)
    {
        input = Quadro11InputListExample.GetExample().First();

        string name = input.Name ?? "structure";

        // MUST be lowercase — quadro14G.exe treats uppercase 'T' as T3 (thymidine DNA residue)
        // which causes ERROR 2. Lowercase 't' is parsed as normal thymidine.
        string sequence = input.Sequence.ToLower();

        string structure = input.Structure ?? BuildDefaultStructure(sequence);
        string chi = input.Chi ?? "S...S....S...S.";
        string orient = input.Orient ?? "A+;B+";

        // Rise: use invariant culture to always get dot separator (not comma on Polish OS)
        string rise = input.Rise > 0
            ? input.Rise.ToString("F1", CultureInfo.InvariantCulture)
            : "3.4";

        // Twist: integer when value has no fractional part; always dot separator
        string twist = input.Twist > 0
            ? (input.Twist % 1 == 0
                ? ((int)input.Twist).ToString()
                : input.Twist.ToString("F1", CultureInfo.InvariantCulture))
            : "30";

        // Path is List<string> — must be joined with semicolons, NOT .ToString()
        string pathStr = input.Path is { Count: > 0 }
            ? string.Join(";", input.Path)
            : string.Empty;

        string test = input.isTest ? "y" : "n";
        string rmLevel = input.RM_Level > 0 ? input.RM_Level.ToString() : "0";
        string iterations = input.Iterations > 0 ? input.Iterations.ToString() : "100";

        // Field names must match exactly what quadro14G.exe reads:
        // "test" (not "isTest"), "rm_level" (not "RM_Level"), "iteration" (not "Iterations")
        return
            $"name\t\t{name}\n" +
            $"sequence\t{sequence}\n" +
            $"structure\t{structure}\n" +
            $"chi\t\t{chi}\n" +
            $"orient\t\t{orient}\n" +
            $"rise\t\t{rise}\n" +
            $"twist\t\t{twist}\n" +
            $"path\t\t{pathStr}\n" +
            $"test\t\t{test}\n" +
            $"rm_level\t{rmLevel}\n" +
            $"iteration\t{iterations}\n";
    }

    private static string BuildDefaultStructure(string sequence)
    {
        // sequence is already lowercase at this point
        var sb = new System.Text.StringBuilder();
        int strandIdx = 0;
        char[] labels = ['A', 'B'];
        foreach (char c in sequence)
            sb.Append(c == 'g' ? labels[strandIdx++ % 2] : '.');
        return sb.ToString();
    }

    /// <summary>
    /// Runs the quadro14G.exe computation in a persistent Docker container.
    ///
    /// Strategy:
    ///   1.  docker run -d --name q14g_{jobId}_{struct} --entrypoint /bin/sh ... -c "tail -f /dev/null"
    ///         → start container, keep alive without TTY (image ENTRYPOINT=/bin/bash exits without -t)
    ///   1b. poll docker inspect until state == "running"
    ///   2.  docker exec  cp /data/file.inp /opt/bin/
    ///   3.  docker exec  ls -lh /opt/bin/          (debug: verify file landed)
    ///   4.  docker exec  bash -c "cd /opt/bin && ./quadro14G.exe file.inp"
    ///   5.  docker exec  ls -lh /opt/bin/          (debug: check output files)
    ///   6.  docker exec  bash -c "cp /opt/bin/*.pdb /data/ || true"
    ///   7.  docker exec  ls -lh /data/             (debug: confirm PDB in /data)
    ///   8.  docker rm -f                           (always, in finally)
    ///
    /// Container name is unique per jobId+structIndex — parallel jobs never collide.
    /// </summary>
    private async Task<byte[]?> RunDockerAsync(string jobId, string jobDir, string inpFileName)
    {
        string containerName = $"q14g_{jobId}_{Path.GetFileNameWithoutExtension(inpFileName)}";
        string mountPath = jobDir.Replace('\\', '/');

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(DockerTimeoutSeconds));

        // ── Step 1: Start container in detached mode (no --rm, no TTY) ────────
        _logger.LogDebug("Job {JobId} [{Container}]: STEP 1 — starting container (detached)", jobId, containerName);

        // --entrypoint /bin/sh overrides the image's /bin/bash which exits immediately
        // when there is no TTY attached (-t). "tail -f /dev/null" keeps the container
        // alive indefinitely without consuming CPU.
        var (startCode, startOut, startErr) = await DockerRunAsync(
            jobId, containerName, cts.Token,
            "run", "-d",
            "--name", containerName,
            "--entrypoint", "/bin/sh",
            "-v", $"{mountPath}:/data",
            DockerImage,
            "-c", "tail -f /dev/null"
        );

        LogDockerStep(jobId, containerName, "run -d", startCode, startOut, startErr);

        if (startCode != 0)
            throw new DockerException(
                $"[{containerName}] Failed to start container (exit {startCode})",
                $"STDOUT:\n{startOut}\nSTDERR:\n{startErr}");

        // ── Step 1b: Wait until container state == "running" ──────────────────
        _logger.LogDebug("Job {JobId} [{Container}]: STEP 1b — waiting for 'running' state", jobId, containerName);
        await WaitForContainerRunningAsync(jobId, containerName, cts.Token);

        try
        {
            // ── Step 2: Copy .inp into /opt/bin ───────────────────────────────
            _logger.LogDebug("Job {JobId} [{Container}]: STEP 2 — copying .inp to /opt/bin", jobId, containerName);
            await DockerExecCheckedAsync(jobId, containerName, cts.Token,
                "cp", $"/data/{inpFileName}", $"/opt/bin/{inpFileName}");

            // ── Step 3: Debug — list /opt/bin before run ──────────────────────
            _logger.LogDebug("Job {JobId} [{Container}]: STEP 3 — listing /opt/bin (pre-run)", jobId, containerName);
            await DockerExecDebugAsync(jobId, containerName, cts.Token,
                "ls", "-lh", "/opt/bin/");

            // ── Step 4: Run quadro14G.exe ─────────────────────────────────────
            _logger.LogDebug("Job {JobId} [{Container}]: STEP 4 — executing quadro14G.exe {Inp}", jobId, containerName, inpFileName);
            await DockerExecCheckedAsync(jobId, containerName, cts.Token,
                "/bin/bash", "-c", $"cd /opt/bin && ./quadro14G.exe {inpFileName}");

            // ── Step 5: Debug — list /opt/bin after run ───────────────────────
            _logger.LogDebug("Job {JobId} [{Container}]: STEP 5 — listing /opt/bin (post-run)", jobId, containerName);
            await DockerExecDebugAsync(jobId, containerName, cts.Token,
                "ls", "-lh", "/opt/bin/");

            // ── Step 6: Copy PDB files back to /data ──────────────────────────
            _logger.LogDebug("Job {JobId} [{Container}]: STEP 6 — copying *.pdb to /data", jobId, containerName);
            await DockerExecCheckedAsync(jobId, containerName, cts.Token,
                "/bin/bash", "-c", "cp /opt/bin/*.pdb /data/ 2>/dev/null || true");

            // ── Step 7: Debug — list /data to confirm PDB arrived ─────────────
            _logger.LogDebug("Job {JobId} [{Container}]: STEP 7 — listing /data (confirm PDB)", jobId, containerName);
            await DockerExecDebugAsync(jobId, containerName, cts.Token,
                "ls", "-lh", "/data/");

            // ── Read resulting PDB from host-side job directory ───────────────
            var pdbFiles = Directory.GetFiles(jobDir, "*.pdb");
            if (pdbFiles.Length == 0)
            {
                _logger.LogWarning("Job {JobId} [{Container}]: no .pdb found. jobDir contents: {Files}",
                    jobId, containerName,
                    string.Join(", ", Directory.GetFiles(jobDir).Select(Path.GetFileName)));
                return null;
            }

            // Prefer Xplor-refined file (contains "_xplor"), else take first
            string best = pdbFiles.FirstOrDefault(f => f.Contains("xplor")) ?? pdbFiles[0];
            _logger.LogInformation("Job {JobId} [{Container}]: returning {File}", jobId, containerName, Path.GetFileName(best));
            return await System.IO.File.ReadAllBytesAsync(best);
        }
        finally
        {
            // ── Step 8: Always stop and remove container ──────────────────────
            _logger.LogDebug("Job {JobId} [{Container}]: STEP 8 — removing container", jobId, containerName);
            await StopAndRemoveContainerAsync(jobId, containerName);
        }
    }

    // ── Docker command helpers ────────────────────────────────────────────────

    /// <summary>
    /// Runs any docker command and returns (exitCode, stdout, stderr).
    /// </summary>
    private async Task<(int exitCode, string stdout, string stderr)> DockerRunAsync(
        string jobId, string containerName, CancellationToken ct, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"[{containerName}] Docker timeout after {DockerTimeoutSeconds}s");
        }

        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>
    /// Runs "docker exec container execArgs", logs output, throws DockerException on non-zero exit.
    /// </summary>
    private async Task DockerExecCheckedAsync(
        string jobId, string containerName, CancellationToken ct, params string[] execArgs)
    {
        var args = new[] { "exec", containerName }.Concat(execArgs).ToArray();
        var (code, stdout, stderr) = await DockerRunAsync(jobId, containerName, ct, args);
        LogDockerStep(jobId, containerName, string.Join(" ", execArgs), code, stdout, stderr);

        if (code != 0)
            throw new DockerException(
                $"[{containerName}] exec '{execArgs[0]}' exited with code {code}. " +
                $"Last stderr: {stderr.Trim().Split('\n').LastOrDefault()}",
                $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    /// <summary>
    /// Same as DockerExecCheckedAsync but ignores non-zero exit — used for diagnostic ls commands.
    /// </summary>
    private async Task DockerExecDebugAsync(
        string jobId, string containerName, CancellationToken ct, params string[] execArgs)
    {
        var args = new[] { "exec", containerName }.Concat(execArgs).ToArray();
        var (code, stdout, stderr) = await DockerRunAsync(jobId, containerName, ct, args);
        LogDockerStep(jobId, containerName, string.Join(" ", execArgs), code, stdout, stderr);
    }

    /// <summary>
    /// Polls "docker inspect" until the container reaches state "running".
    /// Guards against Windows/WSL race where docker run -d returns exit 0
    /// but the container is not yet exec-able for a brief moment.
    /// </summary>
    private async Task WaitForContainerRunningAsync(string jobId, string containerName, CancellationToken ct)
    {
        const int pollIntervalMs = 150;
        const int maxAttempts = 40; // 40 × 150 ms = 6 s maximum wait

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var (code, stdout, _) = await DockerRunAsync(
                jobId, containerName, ct,
                "inspect", "--format", "{{.State.Status}}", containerName);

            string status = stdout.Trim();

            _logger.LogDebug(
                "Job {JobId} [{Container}]: STEP 1b attempt {Attempt}/{Max}: status='{Status}' exit={Code}",
                jobId, containerName, attempt, maxAttempts, status, code);

            if (code == 0 && status == "running")
                return;

            // "created" = container exists but entrypoint hasn't started yet — keep polling
            // anything else (exited, dead) = hard failure
            if (code == 0 && status is not "created" and not "running")
                throw new DockerException(
                    $"[{containerName}] Container reached unexpected state '{status}' before exec",
                    $"docker inspect status: {status}");

            await Task.Delay(pollIntervalMs, ct);
        }

        throw new TimeoutException(
            $"[{containerName}] Container did not reach 'running' state within {maxAttempts * pollIntervalMs / 1000.0:F1}s");
    }

    /// <summary>
    /// docker rm -f — stops and removes in one shot. Errors are swallowed and logged.
    /// </summary>
    private async Task StopAndRemoveContainerAsync(string jobId, string containerName)
    {
        try
        {
            var (rmCode, rmOut, rmErr) = await DockerRunAsync(
                jobId, containerName, CancellationToken.None,
                "rm", "-f", containerName);

            LogDockerStep(jobId, containerName, $"rm -f {containerName}", rmCode, rmOut, rmErr);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Job {JobId} [{Container}]: could not remove container: {Msg}",
                jobId, containerName, ex.Message);
        }
    }

    /// <summary>
    /// Centralised per-step debug logging: command, exit code, stdout, stderr.
    /// stderr is logged at Warning so it's visible even at default log levels.
    /// </summary>
    private void LogDockerStep(
        string jobId, string containerName,
        string stepLabel, int exitCode,
        string stdout, string stderr)
    {
        _logger.LogDebug(
            "Job {JobId} [{Container}] ▶ {Step} → exit {Code}",
            jobId, containerName, stepLabel, exitCode);

        if (stdout.Length > 0)
            _logger.LogDebug(
                "Job {JobId} [{Container}] ▶ {Step} STDOUT:\n{Out}",
                jobId, containerName, stepLabel, stdout.TrimEnd());

        if (stderr.Length > 0)
            _logger.LogWarning(
                "Job {JobId} [{Container}] ▶ {Step} STDERR:\n{Err}",
                jobId, containerName, stepLabel, stderr.TrimEnd());
    }

    // ── Utility helpers ───────────────────────────────────────────────────────

    private static int CountPdbAtoms(byte[] pdbContent)
    {
        var text = System.Text.Encoding.UTF8.GetString(pdbContent);
        return text.Split('\n').Count(l => l.StartsWith("ATOM") || l.StartsWith("HETATM"));
    }

    private async Task<bool> CheckDockerAvailableAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private async Task<bool> CheckDockerImageExistsAsync(string image)
    {
        try
        {
            var psi = new ProcessStartInfo("docker", $"image inspect {image}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private void CleanupJobDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not delete {Dir}: {Msg}", dir, ex.Message);
        }
    }
}