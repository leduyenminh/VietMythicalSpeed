// Port of VMS.Core.AppVersion – keep both in sync.

/** Parses "1.2.3" (also "1.2", "1.2.3-dev", "v1.2.3") into [major, minor, patch]; null when unparseable. */
export function parseVersion(text: string | undefined): [number, number, number] | null {
  const m = /^v?(\d{1,6})(?:\.(\d{1,6}))?(?:\.(\d{1,6}))?(?:[-+].*)?$/.exec((text ?? '').trim());
  if (!m) return null;
  return [Number(m[1]), Number(m[2] ?? 0), Number(m[3] ?? 0)];
}

export function compareVersions(a: [number, number, number], b: [number, number, number]): number {
  for (let i = 0; i < 3; i++) if (a[i] !== b[i]) return a[i] < b[i] ? -1 : 1;
  return 0;
}

/** True when `version` is at least `minimum`. An empty minimum disables the check; an unparseable version fails it. */
export function meetsMinimum(version: string | undefined, minimum: string): boolean {
  const min = parseVersion(minimum);
  if (!min) return true;
  const v = parseVersion(version);
  return v !== null && compareVersions(v, min) >= 0;
}
