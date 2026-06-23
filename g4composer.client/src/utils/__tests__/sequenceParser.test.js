import { describe, it, expect } from 'vitest'
import {
  orientFromGroup,
  defaultPolarityLabel,
  countTetrads,
  validateEntry,
  parseInputLines,
} from '../sequenceParser.js'

describe('orientFromGroup', () => {
  it('returns all-up for parallel UUUU', () => {
    expect(orientFromGroup('UUUU')).toBe('A+;B+')
  })
  it('falls back to antiparallel for unknown groups', () => {
    expect(orientFromGroup('ZZZZ')).toBe('A+;B-')
    expect(orientFromGroup(undefined)).toBe('A+;B-')
  })
})

describe('defaultPolarityLabel', () => {
  it.each([
    [1, 'R'],
    [2, 'RL'],
    [3, 'RLL'],
    [4, 'RLRL'],
  ])('tetradCount %i → %s', (count, label) => {
    expect(defaultPolarityLabel(count)).toBe(label)
  })
  it('alternates R/L beyond 4 tetrads', () => {
    expect(defaultPolarityLabel(5)).toBe('RLRLR')
  })
})

describe('countTetrads', () => {
  it('returns 0 for empty input', () => {
    expect(countTetrads('')).toBe(0)
  })
  it('counts non-dot/bracket chars divided by 4', () => {
    // 8 tetrad chars (^) → 2 tetrads
    expect(countTetrads('((^^^^^^^^))')).toBe(2)
  })
  it('ignores dots and brackets', () => {
    expect(countTetrads('(((....)))')).toBe(1) // rounds up from 0 via Math.max(1, …)
  })
})

describe('validateEntry', () => {
  const base = { name: 'x', sequence: 'acgt', structure: '....' }

  it('passes a minimal valid entry', () => {
    expect(() => validateEntry(base)).not.toThrow()
  })
  it('rejects missing name', () => {
    expect(() => validateEntry({ ...base, name: '' })).toThrow(/name/i)
  })
  it('rejects too-short sequence', () => {
    expect(() => validateEntry({ ...base, sequence: 'ac' })).toThrow(/too short/i)
  })
  it('rejects invalid sequence chars', () => {
    expect(() => validateEntry({ ...base, sequence: 'acgx' })).toThrow(/invalid characters/i)
  })
  it('rejects missing structure', () => {
    expect(() => validateEntry({ ...base, structure: '' })).toThrow(/structure/i)
  })
})

describe('parseInputLines — 3-line shorthand', () => {
  it('parses >name / sequence / structure', () => {
    const r = parseInputLines('>143d_basket\nGGGTTAGGG\n(((...)))')
    expect(r.name).toBe('143d_basket')
    expect(r.sequence).toBe('gggttaggg')   // lowercased
    expect(r.structure).toBe('(((...)))')
    expect(r._format).toBe('3line')
  })

  it('detects RNA (uppercase) → North pucker', () => {
    const r = parseInputLines('>rna\nGGGUUAGGG\n(((...)))')
    expect(r.sugarPucker).toBe('N')
  })

  it('detects DNA (lowercase) → South pucker', () => {
    const r = parseInputLines('>dna\ngggttaggg\n(((...)))')
    expect(r.sugarPucker).toBe('S')
  })

  it('throws when first line lacks ">"', () => {
    expect(() => parseInputLines('143d\nGGGG\n....')).toThrow(/">"/)
  })

  it('throws with fewer than 2 lines', () => {
    expect(() => parseInputLines('>onlyname')).toThrow(/Minimum 2 lines/)
  })
})

describe('parseInputLines — full .inp key/value', () => {
  const inp = [
    'name      pz74',
    'sequence  ggggttttgggg',
    'structure ((((....))))',
    'orient    A+;B-',
    'rise      3.4',
    'twist     29',
    'path      A1;B1;A4;B4',
  ].join('\n')

  it('parses key/value format and splits path', () => {
    const r = parseInputLines(inp)
    expect(r._format).toBe('inp')
    expect(r.name).toBe('pz74')
    expect(r.sequence).toBe('ggggttttgggg')
    expect(r.path).toEqual(['A1', 'B1', 'A4', 'B4'])
    expect(r.rmLevel).toBe(5)
  })
})
