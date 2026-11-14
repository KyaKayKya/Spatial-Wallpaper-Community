"""Reject a stale Player before running acceptance against changed sources."""
from pathlib import Path
import hashlib,json
root=Path(__file__).resolve().parents[2]
def validate():
    receipt=json.loads((root/'Validation/Desktop/build-receipt.json').read_text())
    for name,digest in receipt['sources'].items():
        assert hashlib.sha256((root/name).read_bytes()).hexdigest()==digest,'Stale build source: '+name
    binary=root/'Prototype/Builds/Library'
    for name,digest in receipt['files'].items():
        assert hashlib.sha256((binary/name).read_bytes()).hexdigest()==digest,'Changed Player file: '+name
    return receipt
if __name__=='__main__':validate();print('PASS exact Player build inputs and file hashes')
