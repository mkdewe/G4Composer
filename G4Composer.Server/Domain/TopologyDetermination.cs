namespace G4Composer.Server.Domain;

/// <summary>
/// Explains HOW the candidate topology set was determined for a sequence: the inputs that
/// drive the prediction (tetrad count, the three loop lengths, RNA/DNA, A-rich loops) and the
/// FULL ranked space of sterically-allowed Webba da Silva folds with their literature-prior
/// scores. The top-ranked few (<see cref="ScoredTopology.Built"/>) are the ones actually modelled
/// with Quadro; the rest are shown so the choice of set is transparent.
/// </summary>
public sealed record TopologyDetermination(
    int Tetrads,
    IReadOnlyList<int> Loops,
    bool IsRna,
    IReadOnlyList<bool> ARichLoops,
    IReadOnlyList<ScoredTopology> Ranked);

/// <summary>
/// One topology in the determination space. Admissible folds carry a positive score, a derived
/// probability and (for the top ones) <see cref="Built"/>=true. Folds that were ruled out — by
/// sterics, forbidden loop combinations, inability to thread the four tracts, or a non-positive
/// score — appear with score/probability 0 and a non-null <see cref="ExcludedReason"/>.
/// </summary>
public sealed record ScoredTopology(
    string LoopNotation,
    string Label,
    string Confidence,
    string Rationale,
    int Score,
    double Probability,        // 0–100, score normalised over the whole admissible space
    bool Built,                // true if it was among the top-ranked folds sent to Quadro
    string? ExcludedReason = null);  // non-null ⇒ ruled out (0% fold), with the reason
