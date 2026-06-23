import { describe, it, expect } from 'vitest'
import {
  buildPath,
  buildDefaultOrient,
  buildDefaultTwist,
  buildDefaultRise,
  buildDefaultPucker,
  scaleOrientToTetrads,
  buildChiFromOrient,
} from '../silvaTopology.js'

describe('buildPath', () => {
  it('throws without loop notation', () => {
    expect(() => buildPath('', 2)).toThrow(/required/)
  })
  it('rejects tetrads out of 1-4', () => {
    expect(() => buildPath('+Ln+P+Lw', 5)).toThrow(/1-4/)
    expect(() => buildPath('+Ln+P+Lw', 0)).toThrow(/1-4/)
  })
  it('requires exactly 3 loops', () => {
    expect(() => buildPath('+Ln+P', 2)).toThrow(/Expected 3 loops/)
  })
  it('emits N letters per stop for N tetrads (4 stops)', () => {
    // 1 tetrad → letter "A" only, 4 stops → 4 entries
    const path = buildPath('+Ln+P+Lw', 1)
    expect(path.split(';')).toHaveLength(4)
    expect(path.split(';').every(p => p.startsWith('A'))).toBe(true)
  })
  it('produces 2*4 = 8 entries for 2 tetrads', () => {
    const path = buildPath('+Ln+P+Lw', 2)
    expect(path.split(';')).toHaveLength(8)
  })
})

describe('buildDefaultOrient', () => {
  it.each([
    [1, 'A+'],
    [2, 'A+;B-'],
    [3, 'A+;B-;C-'],
    [4, 'A+;B-;C+;D-'],
  ])('%i tetrads → %s', (n, expected) => {
    expect(buildDefaultOrient(n)).toBe(expected)
  })
  it('throws for invalid tetrad counts', () => {
    expect(() => buildDefaultOrient(5)).toThrow()
  })
})

describe('buildDefaultTwist', () => {
  it.each([
    [1, '29'],
    [2, '19'],
    [3, '19;29'],
    [4, '19;37;19'],
  ])('%i tetrads → %s', (n, expected) => {
    expect(buildDefaultTwist(n)).toBe(expected)
  })
})

describe('buildDefaultRise / buildDefaultPucker', () => {
  it('rise has N-1 steps (min 1)', () => {
    expect(buildDefaultRise(1)).toBe('3.4')
    expect(buildDefaultRise(3)).toBe('3.4;3.4')
  })
  it('pucker repeats the type N-1 times', () => {
    expect(buildDefaultPucker(3, 'N')).toBe('N;N')
    expect(buildDefaultPucker(1, 'S')).toBe('S')
  })
})

describe('scaleOrientToTetrads', () => {
  it('returns unchanged when counts match', () => {
    expect(scaleOrientToTetrads('A+;B-', 2)).toBe('A+;B-')
  })
  it('extends with default "-" for new planes', () => {
    expect(scaleOrientToTetrads('A+;B-', 4)).toBe('A+;B-;C-;D-')
  })
  it('preserves known signs and truncates extra', () => {
    expect(scaleOrientToTetrads('A+;B-;C+;D-', 2)).toBe('A+;B-')
  })
})

describe('buildChiFromOrient', () => {
  it('returns "" on missing inputs', () => {
    expect(buildChiFromOrient('', 'A1', 'A+')).toBe('')
  })
  it('returns "" when tetrad-position count ≠ path entry count', () => {
    // structure has 1 tetrad pos (^), path has 2 entries → mismatch
    expect(buildChiFromOrient('.^.', 'A1;A2', 'A+')).toBe('')
  })
  it('produces a chi string the same length as structure', () => {
    const structure = 'A...A'   // 2 tetrad positions
    const path = 'A1;A2'
    const chi = buildChiFromOrient(structure, path, 'A+')
    expect(chi).toHaveLength(structure.length)
    expect(/^[.S]+$/.test(chi)).toBe(true)
  })
})
