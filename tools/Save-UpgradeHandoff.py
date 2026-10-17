"""Save a stable, portable source/evidence snapshot without copying Unity caches."""
import argparse
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path
import zipfile


def digest(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest().upper()


def save_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--project', type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument('--original', type=Path, required=True)
    parser.add_argument('--phase', choices=('paused', 'checkpoint'), default='paused')
    args = parser.parse_args()
    root = args.project.resolve()
    original = args.original.resolve()
    if original == root:
        raise ValueError('Original and working copy must be different directories')
    stamp = datetime.now().astimezone().strftime('%Y%m%d-%H%M%S')
    destination = root / 'output' / 'upgrade-handoff' / stamp
    destination.mkdir(parents=True, exist_ok=False)
    baseline_dir = root / 'output' / 'upgrade-baseline'
    baseline = json.loads((baseline_dir / 'source-sha256.json').read_text(encoding='utf-8-sig'))
    paths = set(path for path in root.iterdir() if path.is_file())
    for directory in ('Assets', 'Packages', 'ProjectSettings', 'Tests', 'tools', 'docs',
                      'output/upgrade-baseline', 'output/unity-verification',
                      'output/player-verification', 'output/street-verification'):
        paths.update(path for path in (root / directory).rglob('*') if path.is_file())
    for evidence in ('output/upgrade-document-read-20261016.json', 'output/recheck-original-protection.json',
                     'output/recheck-original-protection-final.json',
                     'output/pure-probe/ActualStreetProbeDiag.cs',
                     'output/pure-probe/LegacyStreetTriangulationDiag.cs',
                     'output/pure-probe/RootVehicleEdgeDiagnostic.cs',
                     'output/pure-probe/RootVehicleEdgeDiagnostic-before.txt',
                     'output/pure-probe/RootVehicleEdgeDiagnostic-after.txt'):
        path = root / evidence
        if path.is_file():
            paths.add(path)
    paths.update(path for path in (root / 'output/pure-probe').glob('RootMemoryRouteDiagnostic*') if path.is_file())
    for pattern in ('*Memory*.cs', '*Memory*.ps1', '*Memory*.txt', 'IncomingBatchCoverageDiagnostic.cs', 'RunBaselineRoute.ps1', 'StreetPerceptionBeforeFanMerge.cs'):
        paths.update(path for path in (root / 'output/pure-probe').glob(pattern) if path.is_file())
    # Raw builds are already archived above; also retain their delivered-package mapping.
    for pattern in ('package-manifest.json', '开始体验.txt'):
        paths.update(path for path in (root / 'output').glob('听路试玩_*/' + pattern) if path.is_file())
    paths = sorted(paths, key=lambda path: path.relative_to(root).as_posix())
    manifest = [{'path': path.relative_to(root).as_posix(), 'bytes': path.stat().st_size,
                 'sha256': digest(path)} for path in paths]
    save_json(destination / 'source-and-evidence-sha256.json', manifest)

    current = {item['path']: item for item in manifest}
    old = {item['path'].replace('\\', '/'): item for item in baseline}
    changes = {'changed': [], 'deleted': [], 'added': []}
    for name, item in old.items():
        if name not in current:
            changes['deleted'].append(name)
        elif current[name]['sha256'] != item['sha256']:
            changes['changed'].append(name)
    for name in current:
        if name not in old and name.split('/')[0] in ('Assets', 'Packages', 'ProjectSettings', 'Tests', 'tools'):
            changes['added'].append(name)
    save_json(destination / 'changes-from-baseline.json', changes)

    protections = {'originalRoot': str(original), 'originalTrackedSource': [], 'originalSaves': [],
                   'originalBuildMetadata': [], 'scope': 'Baseline-tracked source SHA256, saved-file SHA256, build length/mtime; not a new hardware or game test'}
    for item in baseline:
        path = original / item['path']
        observed = digest(path) if path.is_file() else None
        protections['originalTrackedSource'].append({'path': str(path), 'unchanged': observed == item['sha256'], 'observedSha256': observed})
    for item in json.loads((baseline_dir / 'protected-saves.json').read_text(encoding='utf-8-sig')):
        path = Path(item['path'])
        observed = digest(path) if path.is_file() else None
        protections['originalSaves'].append({'path': str(path), 'unchanged': observed == item['sha256'], 'observedSha256': observed})
    for item in json.loads((baseline_dir / 'protected-builds.json').read_text(encoding='utf-8-sig')):
        path = original / item['path']
        same = False
        if path.is_file():
            stat = path.stat()
            expected = datetime.fromisoformat(item['writeUtc'].replace('Z', '+00:00')).timestamp()
            same = stat.st_size == item['length'] and abs(stat.st_mtime - expected) < 0.00001
        protections['originalBuildMetadata'].append({'path': str(path), 'unchanged': same})
    protections['allBaselineTrackedUnchanged'] = all(item['unchanged'] for group in ('originalTrackedSource', 'originalSaves', 'originalBuildMetadata') for item in protections[group])
    save_json(destination / 'original-protection-recheck.json', protections)

    missing = [path.relative_to(root).as_posix() for path in (root / 'Assets').rglob('*')
               if path.suffix != '.meta' and not Path(str(path) + '.meta').exists()]
    save_json(destination / 'missing-meta.json', missing)
    results = []
    for path in sorted((root / 'output' / 'unity-verification').glob('*/results.json')):
        data = json.loads(path.read_text(encoding='utf-8-sig'))
        results.append({'path': path.relative_to(root).as_posix(), 'stage': data['stage'], 'status': data['status'], 'reason': data.get('reason')})
    save_json(destination / 'verification-index.json', results)

    archive = destination / 'TingLuStreetUpgrade-source-and-evidence.zip'
    with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=6) as output:
        for path in paths:
            output.write(path, path.relative_to(root).as_posix())
        for path in destination.glob('*.json'):
            output.write(path, 'handoff-evidence/' + path.name)
    with zipfile.ZipFile(archive) as snapshot:
        bad = snapshot.testzip()
        if bad:
            raise RuntimeError('Archive CRC error: ' + bad)
        for item in manifest:
            if hashlib.sha256(snapshot.read(item['path'])).hexdigest().upper() != item['sha256']:
                raise RuntimeError('Archive content mismatch: ' + item['path'])
    changed_during_snapshot = [item['path'] for item in manifest if digest(root / item['path']) != item['sha256']]
    if changed_during_snapshot:
        raise RuntimeError('Files changed during snapshot: ' + ', '.join(changed_during_snapshot))
    protection_passed = protections['allBaselineTrackedUnchanged']
    summary = {'savedAt': datetime.now(timezone.utc).isoformat(),
               'status': 'SAVED_AND_VERIFIED' if protection_passed else 'FAIL',
               'project': str(root), 'fileCount': len(manifest), 'archive': archive.name,
               'archiveBytes': archive.stat().st_size, 'archiveSha256': digest(archive),
               'archiveContentVerified': True, 'snapshotStable': True,
               'originalBaselineProtectionPassed': protection_passed,
               'originalSourceFiles': len(protections['originalTrackedSource']),
               'originalSavedFiles': len(protections['originalSaves']),
               'originalBuildMetadataFiles': len(protections['originalBuildMetadata']),
               'missingMeta': missing, 'changeCounts': {key: len(value) for key, value in changes.items()},
               'development': 'PAUSED_AT_USER_REQUEST' if args.phase == 'paused' else 'DEVELOPMENT_CHECKPOINT',
               'newStreetBuild': 'SEE_VERIFICATION_INDEX',
               'resume': 'docs/upgrade/RESUME.md', 'cacheExcluded': ['Library', 'Temp', 'Logs', 'UserSettings'],
               'warning': 'Snapshot verification is not a game test; consult the verification index and current handoffs for failures and unverified changes'}
    save_json(destination / 'summary.json', summary)
    phase_text = '暂停快照' if args.phase == 'paused' else '开发检查点快照'
    (destination / 'README.txt').write_text('从 docs/upgrade/RESUME.md 继续。\n这是' + phase_text + '，不是可玩新版。\nZIP已逐文件SHA256回读校验，源文件在快照期间保持稳定。\nsummary.json记录保护复核、缺meta、已知验证限制。\n解压到独立空目录，再由匹配版本Unity重建Library。不要覆盖原D盘旧项目或用户存档。\n', encoding='utf-8')
    if not protection_passed:
        raise RuntimeError('Original-project protection recheck failed; handoff archive retained at ' + str(archive))
    print(json.dumps({'directory': str(destination), **summary}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
