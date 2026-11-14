import argparse
import copy
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[3]/'Supervisor/Tests'))
import input_continuity_inputs as checks


class ContinuityInputs(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name);self.author=self.root/'author';self.worker=self.root/'worker'
        self.author.mkdir();(self.worker/'player').mkdir(parents=True)
        self.receipt={name:'a'*64 for name in checks.AUTHOR_PATHS | checks.WORKER_PATHS}
        self.receipt['Packages/com.spatialwallpaper.sdk/Editor/CilboxProgramCompiler.cs']='b'*64
        self.receipt['Packages/com.spatialwallpaper.sdk/Runtime/Execution/Runtime.cs']='c'*64
        manifest={'version':1};build=hashlib.sha256(b'{"version":1}').hexdigest();manifest['build_id']=build
        with zipfile.ZipFile(self.author/'scene.spatial','w') as z:z.writestr('manifest.json',json.dumps(manifest))
        self.meta={'fixture':'input-continuity-14-v1','build':build,'capabilities':['input','private-storage']}
        (self.author/'author.json').write_text(json.dumps(self.meta))
        self.ar={'status':'partial-input-CSharp-export-only','sources':self.receipt.copy(),'author':self.meta.copy(),'package_sha256':checks.digest(self.author/'scene.spatial')}
        (self.worker/'player/Worker.exe').write_bytes(b'fixture')
        self.wr={'status':'production-Worker-built-not-acceptance','sources':self.receipt.copy(),'runtime_files':{'Worker.exe':checks.digest(self.worker/'player/Worker.exe')}}
        self.save()

    def save(self):
        (self.author/'report.json').write_text(json.dumps(self.ar));(self.worker/'report.json').write_text(json.dumps(self.wr))

    def validate(self):return checks.validate_inputs(self.worker,self.author,self.receipt)

    def test_complete_inputs(self):self.validate()

    def test_preflight_uses_the_same_source_set_as_build_receipts(self):
        import check_input_continuity_inputs as preflight
        path=self.root/'Packages/com.spatialwallpaper.sdk/ThirdParty/Cilbox/LICENSE';path.parent.mkdir(parents=True);path.write_text('license input')
        core=self.root/'Prototype/Assets/ContentRuntime/Core/Core.cs';core.parent.mkdir(parents=True);core.write_text('shared core')
        value=preflight.source_receipt(self.root)
        self.assertIn(path.relative_to(self.root).as_posix(),value)
        self.assertIn(core.relative_to(self.root).as_posix(),value)

    def test_all_filtered_dependencies_reject_change_removal_and_extra(self):
        for role,report in [('author',self.ar),('worker',self.wr)]:
            original=copy.deepcopy(report['sources'])
            for path in checks.dependencies(original,role):
                for operation in ['changed','missing','removed-from-current']:
                    with self.subTest(role=role,path=path,operation=operation):
                        report['sources']=original.copy();current=self.receipt.copy()
                        if operation=='changed':report['sources'][path]='0'*64
                        elif operation=='missing':report['sources'].pop(path)
                        else:current.pop(path)
                        self.save()
                        with self.assertRaises(ValueError):checks.validate_inputs(self.worker,self.author,current)
            report['sources']=original
            report['sources']['Packages/com.spatialwallpaper.sdk/Runtime/Deleted.cs']='0'*64
            self.save()
            with self.assertRaises(ValueError):self.validate()
            report['sources']=original;report['sources'].pop('Packages/com.spatialwallpaper.sdk/Runtime/Deleted.cs',None);self.save()

    def test_metadata_build_status_and_inventory_rejected(self):
        original=copy.deepcopy(self.ar);worker=copy.deepcopy(self.wr)
        for kind in ['metadata','build','author-status','worker-status','diagnostic','inventory']:
            with self.subTest(kind=kind):
                self.ar=copy.deepcopy(original);self.wr=copy.deepcopy(worker)
                if kind=='metadata':self.ar['author']['fixture']='input-runtime'
                elif kind=='build':
                    self.ar['author']['build']='0'*64
                    (self.author/'author.json').write_text(json.dumps(self.ar['author']))
                elif kind=='author-status':self.ar['status']='failed'
                elif kind=='worker-status':self.wr['status']='failed'
                elif kind=='diagnostic':self.wr['release_candidate']=False
                else:self.wr['runtime_files']={}
                self.save()
                with self.assertRaises(ValueError):self.validate()
                (self.author/'author.json').write_text(json.dumps(self.meta))

    def test_modes_fail_closed_for_all_extra_active_options(self):
        base=dict(interaction_seconds=1800,interaction_drop_release=False,desktop=True,background_injection=True,worker='x',author='y')
        checks.validate_modes(argparse.Namespace(**base))
        for name in ['compile_only','trace_self_test','injector_self_test','diagnostic_worker','negative_trace_sleep','automatic_recovery','soak_seconds','unknown_future_mode']:
            with self.subTest(name=name),self.assertRaises(ValueError):checks.validate_modes(argparse.Namespace(**base,**{name:True}))
        short=dict(base,interaction_seconds=1,interaction_drop_release=True);checks.validate_modes(argparse.Namespace(**short))
        for seconds in [0,-1,1801,True,1.0]:
            with self.subTest(seconds=seconds),self.assertRaises(ValueError):checks.validate_modes(argparse.Namespace(**dict(short,interaction_seconds=seconds)))
        with self.assertRaises(ValueError):checks.validate_modes(argparse.Namespace(**dict(base,interaction_drop_release=True)))

    def test_every_registered_conflicting_mode_is_rejected(self):
        patch=Path(__file__).resolve().parents[3]/'docs/work/sdk-runtime/input-continuity-preflight.patch'
        line=next(line[1:] for line in patch.read_text().splitlines() if line.startswith(' parser=argparse.ArgumentParser();'))
        namespace={'argparse':argparse,'Path':Path}
        exec(line.split(';args=parser.parse_args()')[0], namespace)
        parser=namespace['parser']
        base=parser.parse_args(['--worker','worker','--author','author','--desktop','--background-injection','--interaction-seconds','1800'])
        checks.validate_modes(base)
        allowed={'help','worker','author','desktop','background_injection','interaction_seconds','interaction_drop_release'}
        for action in parser._actions:
            if action.dest in allowed:continue
            with self.subTest(mode=action.dest):
                values=vars(base).copy()
                values[action.dest]=action.choices[0] if action.choices else 1 if action.type is int else True
                with self.assertRaises(ValueError):checks.validate_modes(argparse.Namespace(**values))


if __name__=='__main__':unittest.main()
