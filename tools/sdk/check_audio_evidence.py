"""Read existing audio observations; never launch audio, Unity, or desktop code.

This is receipt validation, not a new execution or E acceptance. --reports are
explicit artifact directory names, and are never offered to verify_e as fresh runs.
"""
import argparse
import importlib.util
import json
from pathlib import Path
import uuid

from audio_evidence import validate_report, validate_observation, validate_inputs, digest, need
from provenance import ROOT, sources
from verify_e import inventory


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', type=Path, required=True)
    parser.add_argument('--reports', nargs='+', required=True)
    args = parser.parse_args()
    repository = args.repository.resolve()
    spec = importlib.util.spec_from_file_location('audio_audit_provenance', repository / 'tools/sdk/provenance.py')
    provenance = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(provenance)
    current = provenance.sources()
    out = ROOT / 'Validation/Content/sdk-runtime/E/audio-evidence' / uuid.uuid4().hex
    out.mkdir(parents=True)
    report = dict(status='failed', scope='read-only historical receipt validation; not new audio execution or passed E',
                  sources=sources(), repository=str(repository), cases=[])
    try:
        for name in args.reports:
            folder = repository / 'Validation/Content/sdk-runtime/E/audio-host' / name
            need(folder.parent == folder.resolve().parent and folder.name == name, 'report-directory')
            data = json.loads((folder / 'report.json').read_text())
            row = dict(report=str(folder / 'report.json'), sha256=digest(folder / 'report.json'))
            if data.get('status') == 'partial-configured-audio-Host':
                mode = 'pause' if data.get('boundary') == 'sampler-pause' else 'tree' if data.get('treeSource') else 'direct'
                if 'input_binding' not in data or 'negativeStabilityVerdict' not in data:
                    validate_observation('audio-host-' + mode, data, folder)
                    row.update(mode=mode, observation='behavior fields accepted only',
                               new_receipt='unavailable: original report lacks required mode/input binding')
                    report['cases'].append(row)
                    continue
                config = json.loads((folder / 'config.json').read_text())
                worker = Path(config['image']).parent
                authors = [path.parent for path in (repository / 'Validation/Content/sdk-runtime/E/audio-author').glob('*/report.json')
                           if digest(path) == data['author_report_sha256']]
                need(len(authors) == 1, 'audio-report-author-identity')
                need(digest(worker / 'report.json') == data['worker_report_sha256'], 'audio-report-worker-identity')
                upstream = {key: dict(path=str(path/'report.json'), sha256=digest(path/'report.json'),
                                      inventory_scope='directory', files=inventory(path))
                            for key, path in [('worker', worker), ('audio-author', authors[0])]}
                # Historical inspection has no fresh-run authority; it still
                # compares the receipt with the referenced files, not itself.
                validate_report('audio-host-' + mode, data, folder, current, upstream)
                _, _, binding = validate_inputs(worker, authors[0], current)
                row.update(mode=mode, observation='accepted', input_binding=binding)
            else:
                # Older negative reports lack author receipt hashes. Do not
                # manufacture provenance for them from neighboring artifacts.
                for key in ('childrenExited', 'samplerExited'):
                    need(data.get(key) is True, 'negative-cleanup:' + key)
                need(data.get('cleanupErrors') == [], 'negative-cleanup-errors')
                if data.get('negativeNoPause') is True:
                    need(data.get('boundary') == 'sampler-pause' and
                         data.get('failure', {}).get('message') == 'sampler-watchdog-kernel-exit-missing', 'negative-pause-oracle')
                    observed = data.get('samplerObservation', {})
                    need(observed.get('pauseInjected') is False and observed.get('exitedBeforeFallback') is False, 'negative-pause-observation')
                    mode = 'negative-no-pause'
                else:
                    need('author-stable-zero-proof-rejected' in (folder / 'client.log').read_text(), 'negative-verdict-oracle')
                    mode = 'negative-verdict'
                row.update(mode=mode, observation='expected failure oracle and cleanup present',
                           input_binding='unavailable in original negative report')
            report['cases'].append(row)
        report['status'] = ('unavailable-new-receipt' if any(row.get('new_receipt', '').startswith('unavailable') or
                            row.get('input_binding') == 'unavailable in original negative report' for row in report['cases'])
                            else 'passed-read-only-receipt-checks')
    except Exception as error:
        report['error'] = repr(error)
        raise
    finally:
        (out / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
        print(out, flush=True)


if __name__ == '__main__':
    main()
