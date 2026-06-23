import { describe, it, expect } from 'vitest'
import { serializeInp, parseInp } from '../inpSerializer.js'

describe('serializeInp', () => {
  it('emits all twelve .inp fields ending with a newline', () => {
    const out = serializeInp({
      name: 'pz74',
      sequence: 'ggggttttgggg',
      structure: '((((....))))',
      orient: 'A+;B-',
      rise: '3.4',
      twist: '29',
      path: ['A1', 'B1', 'A4', 'B4'],
      iterationSteps: [50, 100],
    })
    expect(out.endsWith('\n')).toBe(true)
    for (const key of ['name', 'sequence', 'structure', 'chi', 'shugar',
                        'orient', 'rise', 'twist', 'path', 'test',
                        'rm_level', 'iteration_steps']) {
      expect(out).toMatch(new RegExp(`^${key}\\s`, 'm'))
    }
  })

  it('joins a path array with ";"', () => {
    const out = serializeInp({ name: 'x', sequence: 'gggg', path: ['A1', 'B2'] })
    expect(out).toMatch(/^path\s+A1;B2\s*$/m)
  })

  it('auto-fills chi with dots matching sequence length', () => {
    const out = serializeInp({ name: 'x', sequence: 'gggg' })
    expect(out).toMatch(/^chi\s+\.{4}\s*$/m)
  })

  it('forces test=n and defaults rm_level=5', () => {
    const out = serializeInp({ name: 'x', sequence: 'gggg' })
    expect(out).toMatch(/^test\s+n\s*$/m)
    expect(out).toMatch(/^rm_level\s+5\s*$/m)
  })
})

describe('parseInp', () => {
  it('parses key/value back into an object', () => {
    const r = parseInp([
      'name      pz74',
      'sequence  ggggttttgggg',
      'structure ((((....))))',
      'orient    A+;B-',
      'iteration_steps 20,40,60',
    ].join('\n'))
    expect(r.name).toBe('pz74')
    expect(r.sequence).toBe('ggggttttgggg')
    expect(r.iterationSteps).toEqual([20, 40, 60])
  })

  it('ignores comment lines and applies defaults', () => {
    const r = parseInp('# a comment\nname x\nsequence gggg')
    expect(r.name).toBe('x')
    expect(r.orient).toBe('A+;B-')
    expect(r.twist).toBe('29')
    expect(r.iterationSteps).toBeNull()
  })
})

describe('serializeInp ↔ parseInp round-trip', () => {
  it('preserves the core fields', () => {
    const original = {
      name: 'roundtrip',
      sequence: 'ggggttttgggg',
      structure: '((((....))))',
      orient: 'A+;B-',
      rise: '3.4',
      twist: '29',
      path: ['A1', 'B1', 'A4', 'B4'],
      iterationSteps: [50, 100],
    }
    const reparsed = parseInp(serializeInp(original))
    expect(reparsed.name).toBe(original.name)
    expect(reparsed.sequence).toBe(original.sequence)
    expect(reparsed.structure).toBe(original.structure)
    expect(reparsed.orient).toBe(original.orient)
    expect(reparsed.twist).toBe(original.twist)
    expect(reparsed.path).toBe('A1;B1;A4;B4') // parseInp keeps path as raw string
    expect(reparsed.iterationSteps).toEqual([50, 100])
  })
})
