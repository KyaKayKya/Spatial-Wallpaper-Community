"""Revalidate and copy a fixed production Worker for a fresh acceptance execution.

Only the binary build is reused. Every Host, Worker process, attempt, author
effect and lifecycle observation must still be newly executed by the caller.
"""
from pathlib import Path
import argparse,json,shutil,uuid
from provenance import ROOT,sources
from verify_delivery import component,digest,inventory
from release_identity import identity
from assembly_inventory import identities

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--worker',type=Path,required=True);args=p.parse_args()
 worker=args.worker.resolve();version=identity();version.pop('package_sha256');receipt=sources()
 original=component(worker,'worker',version)
 actual=identities([worker/'player/Worker_Data/Managed/SpatialWallpaper.Sdk.dll'])
 if list(actual.values())!=[version]:raise ValueError('reused-Worker-actual-assembly-identity')
 out=ROOT/'Validation/Content/sdk-runtime/D/worker'/uuid.uuid4().hex;out.mkdir(parents=True)
 shutil.copytree(worker/'player',out/'player');shutil.copytree(worker/'dependencies',out/'dependencies');shutil.copyfile(worker/'report.json',out/'source-build-receipt.json')
 if inventory(out/'player')!=original['runtime_files'] or sources()!=receipt:raise ValueError('reused-Worker-copy-or-source-drift')
 report=dict(original,sources=receipt,original_build_sources=original['sources'],build_reused=True,
             revalidated_build={'path':str(worker),'receipt_sha256':digest(worker/'report.json'),
                                'scope':'same production binary; related compilation inputs, identity, dependencies and full runtime inventory revalidated; not a fresh Unity build or acceptance result'})
 (out/'report.json').write_text(json.dumps(report,indent=2));print(out,flush=True)

if __name__=='__main__':main()
