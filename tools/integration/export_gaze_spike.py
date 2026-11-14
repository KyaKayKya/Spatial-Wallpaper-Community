"""Authoring only: strict safetensor load, official architecture, ONNX + reference."""
import hashlib
import importlib.util
import json
import struct
from pathlib import Path

import numpy as np
import torch
from torchvision.models.resnet import Bottleneck

ROOT=Path(__file__).resolve().parents[2]/'Validation/Gaze/local'
def main():
    torch.set_num_threads(4)
    path=ROOT/'l2cs_gaze360_resnet50.safetensors'
    if hashlib.sha256(path.read_bytes()).hexdigest()!='75405bdc01f7086b3887280fe50c28a14bc62463a53fa749781e4a3cf98eca0e':raise ValueError('Unexpected checkpoint')
    raw=path.read_bytes(); size=struct.unpack('<Q',raw[:8])[0]; header=json.loads(raw[8:8+size]); start=8+size
    state={}
    for name,desc in header.items():
        if name=='__metadata__':continue
        dtype={'F32':'<f4','I64':'<i8'}[desc['dtype']]
        a,b=desc['data_offsets']
        state[name]=torch.from_numpy(np.frombuffer(raw[start+a:start+b],dtype=dtype).reshape(desc['shape']).copy())
    spec=importlib.util.spec_from_file_location('official_model',ROOT/'official_model.py')
    if hashlib.sha256((ROOT/'official_model.py').read_bytes()).hexdigest()!='2fcf682d880bc22a4686b22ff89d48d0a46429ce47e6025a3aeaf1e6fb6fc2c0':raise ValueError('Unexpected source')
    source=importlib.util.module_from_spec(spec);spec.loader.exec_module(source)
    model=source.L2CS(Bottleneck,[3,4,6,3],90).eval()
    model.load_state_dict(state,strict=True)
    x=np.random.default_rng(47).normal(0,1,(1,3,448,448)).astype(np.float32)
    with torch.no_grad():reference=model(torch.from_numpy(x))
    np.savez(ROOT/'export-reference.npz',input=x,head0=reference[0].numpy(),head1=reference[1].numpy())
    out=ROOT/'l2cs_net_1x3x448x448.onnx'
    torch.onnx.export(model,torch.from_numpy(x),str(out),input_names=['face'],output_names=['head0','head1'],opset_version=13,dynamo=False)
    report={'torch':torch.__version__,'parameters':sum(p.numel() for p in model.parameters()),'onnx_bytes':out.stat().st_size,'onnx_sha256':hashlib.sha256(out.read_bytes()).hexdigest(),'checkpoint_sha256':hashlib.sha256(raw).hexdigest(),'output_semantics':'raw logits; official Pipeline assigns first output pitch and second yaw despite model variable names; geometry mapping still needs real direction check','accuracy_verified':False}
    (ROOT/'export-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8'); print(json.dumps(report,indent=2))
if __name__=='__main__':main()
