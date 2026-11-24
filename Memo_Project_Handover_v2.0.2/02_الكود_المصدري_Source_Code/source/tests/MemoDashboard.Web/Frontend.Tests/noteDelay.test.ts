import { describe, it, expect } from 'vitest';
import { calculateNoteDelay, ALLOWED_DELAY_DAYS } from '../../../src/MemoDashboard.Web/Frontend/src/utils/noteDelay';

describe('calculateNoteDelay', () => {
  it('returns missing writing date message when writingDate is null, undefined, or empty', () => {
    const r1 = calculateNoteDelay(null, '2026-03-10');
    expect(r1.hasWritingDate).toBe(false);
    expect(r1.isDelayed).toBe(false);
    expect(r1.displayText).toBe('تاريخ التحرير غير مدخل');

    const r2 = calculateNoteDelay('', '2026-03-10');
    expect(r2.hasWritingDate).toBe(false);
    expect(r2.isDelayed).toBe(false);
    expect(r2.displayText).toBe('تاريخ التحرير غير مدخل');
  });

  it('handles invalid date order (arrival before writing)', () => {
    const res = calculateNoteDelay('2026-03-15', '2026-03-10');
    expect(res.hasWritingDate).toBe(true);
    expect(res.isDelayed).toBe(false);
    expect(res.displayText).toBe('تاريخ الورود يسبق تاريخ التحرير');
  });

  it('marks note as on-time within allowed 5 days', () => {
    // 0 days (same day)
    const r0 = calculateNoteDelay('2026-03-10', '2026-03-10');
    expect(r0.isDelayed).toBe(false);
    expect(r0.daysDelayed).toBe(0);
    expect(r0.displayText).toContain('ورود في نفس اليوم');

    // 3 days
    const r3 = calculateNoteDelay('2026-03-10', '2026-03-13');
    expect(r3.isDelayed).toBe(false);
    expect(r3.daysDelayed).toBe(3);
    expect(r3.displayText).toContain('طبيعي (3 يوماً)');

    // 5 days exactly
    const r5 = calculateNoteDelay('2026-03-10', '2026-03-15');
    expect(r5.isDelayed).toBe(false);
    expect(r5.daysDelayed).toBe(ALLOWED_DELAY_DAYS);
    expect(r5.displayText).toContain('طبيعي (5 يوماً)');
  });

  it('marks note as delayed when difference exceeds 5 days', () => {
    // 6 days (1 day excess)
    const r6 = calculateNoteDelay('2026-03-10', '2026-03-16');
    expect(r6.isDelayed).toBe(true);
    expect(r6.daysDelayed).toBe(6);
    expect(r6.displayText).toContain('متأخرة 6 يوماً');
    expect(r6.displayText).toContain('تجاوز بمقدار 1 يوم');

    // 12 days (7 days excess)
    const r12 = calculateNoteDelay('2026-03-01', '2026-03-13');
    expect(r12.isDelayed).toBe(true);
    expect(r12.daysDelayed).toBe(12);
    expect(r12.displayText).toContain('متأخرة 12 يوماً');
    expect(r12.displayText).toContain('تجاوز بمقدار 7 أيام');
  });
});
