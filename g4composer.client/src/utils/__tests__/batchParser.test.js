import { describe, it, expect } from 'vitest'
import { parseText } from '../batchParser.js'

// Minimal Silva classification data needed by the 5-line "minimal" format.
const silvaData = [
  { code: 'UDDU', subtypes: [{ code: '11a', loop: '+Ln+P+Lw' }] },
]

describe('parseText — minimal (5-line) format', () => {
  const block = [
    '143d',
    'agggttagggttagggttaggg',
    '(((^^...^^...^^...^^)))',
    'UDDU',
    '11a',
  ].join('\n')

  it('parses and auto-derives path/orient/twist', () => {
    const [item] = parseText(block, 'minimal.inp', silvaData)
    expect(item.error).toBeUndefined()
    expect(item.input.name).toBe('143d')
    expect(item.input.sequence).toBe('agggttagggttagggttaggg')
    expect(Array.isArray(item.input.path)).toBe(true)
    expect(item.input.orient).toBeTruthy()
    expect(item.input.iterationSteps).toEqual([100])
  })

  it('rejects an unknown topology code', () => {
    const bad = block.replace('UDDU', 'ZZZZ')
    const [item] = parseText(bad, 'bad.inp', silvaData)
    expect(item.error).toMatch(/Unknown topology/)
  })

  it('rejects a subtype not in the group', () => {
    const bad = block.replace('11a', '99z')
    const [item] = parseText(bad, 'bad.inp', silvaData)
    expect(item.error).toMatch(/not part of group/)
  })
})

describe('parseText — full positional (11-line) format', () => {
  const block = [
    '3a',
    'ggggttttgggg',
    '((((....))))',
    '............',
    'A+;B-',
    '3.4',
    '29',
    'A1;B1;A4;B4',
    'n',
    '5',
    '100',
  ].join('\n')

  it('parses positional fields', () => {
    const [item] = parseText(block, 'full.inp', silvaData)
    expect(item.error).toBeUndefined()
    expect(item.input.name).toBe('3a')
    expect(item.input.path).toEqual(['A1', 'B1', 'A4', 'B4'])
    expect(item.input.rmLevel).toBe(5)
    expect(item.input.iterationSteps).toEqual([100])
  })

  it('rejects a zero/invalid rise', () => {
    const bad = block.replace('3.4', '0')
    const [item] = parseText(bad, 'full.inp', silvaData)
    expect(item.error).toMatch(/rise/)
  })
})

describe('parseText — key/value .inp format', () => {
  it('treats a key/value file with blank lines as ONE block', () => {
    const text = [
      'name      pz74',
      '',
      'sequence  ggggttttgggg',
      '',
      'structure ((((....))))',
    ].join('\n')
    const items = parseText(text, 'pz74.inp', silvaData)
    expect(items).toHaveLength(1)
    expect(items[0].input.name).toBe('pz74')
  })

  it('reports missing required fields', () => {
    const text = 'name x\nstructure ((((....))))'
    const [item] = parseText(text, 'x.inp', silvaData)
    expect(item.error).toMatch(/sequence/)
  })
})

describe('parseText — splitting and errors', () => {
  it('returns a "no usable blocks" item for empty input', () => {
    const [item] = parseText('   ', 'empty.inp', silvaData)
    expect(item.error).toMatch(/no usable blocks/)
  })

  it('errors on a block with an unexpected line count', () => {
    const [item] = parseText('justname\nggggttttgggg\n((((....))))', 'odd.inp', silvaData)
    expect(item.error).toMatch(/expected exactly/)
  })

  it('isolates errors per block (one bad block does not stop the rest)', () => {
    // two minimal blocks separated by a blank line; the second has a bad topology
    const good = '143d\nagggttagggttagggttaggg\n(((^^...^^...^^...^^)))\nUDDU\n11a'
    const bad  = '99x\nagggttagggttagggttaggg\n(((^^...^^...^^...^^)))\nZZZZ\n11a'
    const items = parseText(`${good}\n\n${bad}`, 'multi.inp', silvaData)
    expect(items).toHaveLength(2)
    expect(items[0].error).toBeUndefined()
    expect(items[1].error).toMatch(/Unknown topology/)
  })
})
