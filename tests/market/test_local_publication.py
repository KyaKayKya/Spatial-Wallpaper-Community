import base64
import copy
import json
import os
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock, patch
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from tools.market import local_publication as local, publish
from tools.market.common import MarketError, canonical
from test_published import NOW, BODY, candidate, report


class LocalPublicationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.key = rsa.generate_private_key(public_exponent=65537, key_size=3072)
        cls.pem = cls.key.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption())

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        numbers = self.key.public_key().public_numbers()
        encoded = lambda n: base64.b64encode(n.to_bytes((n.bit_length() + 7) // 8, 'big')).decode()
        base = 'https://github.com/' + local.COMMUNITY + '/releases/download/market-index/'
        self.trust = self.root / 'trust.json'
        self.trust.write_bytes(canonical({'repository': local.COMMUNITY, 'catalogUrl': base + 'published.json', 'signatureUrl': base + 'published.sig.json', 'keyId': 'test', 'modulus': encoded(numbers.n), 'exponent': encoded(numbers.e)}))
        self.validator = self.root / 'validator.json'
        self.validator.write_bytes(canonical({'validator': 'inert-test-validator', 'validatorSha256': 'a' * 64, 'platformIndex': 'inert-index', 'platformIndexSha256': 'b' * 64, 'runtimeFiles': {}}))
        self.gh = Mock()
        self.gh.get.side_effect = lambda path: ({'full_name': local.COMMUNITY, 'private': False} if path.endswith(local.COMMUNITY) else {'sha': 'a' * 40}, {})
        self.gh.asset.side_effect = lambda asset: (asset['browser_download_url'], [BODY])

    def collect(self):
        value = candidate()
        entry = value['entries'][0]
        observations = [{'repositoryId': entry['repositoryId'], 'assetId': entry['build']['assets'][0]['id'], 'work': entry['key'], 'buildId': entry['build']['identity'], 'downloadCount': 100, 'status': 'installable', 'volumeGroup': None}]
        args = SimpleNamespace(output=self.root / 'collected', trust=self.trust, source_sha='a' * 40, validator_config=self.validator, details=None)
        with patch.object(local, 'previous', return_value=(None, None)), patch.object(publish, 'collect', return_value=(value, observations, {entry['key']: {}})), patch.object(publish, 'NativeAdmission', return_value=report), patch.object(publish, 'verify_live_bindings') as live, patch.object(local, 'unprotect_dpapi', side_effect=AssertionError('collect accessed signing key')):
            self.collected = local.collect_stage(args, gh=self.gh, now=NOW)
        self.assertEqual(live.call_count, 1)
        self.directory = args.output
        return self.collected

    def sign(self, key=None):
        self.collect()
        args = SimpleNamespace(collection=self.directory, collection_sha256=self.collected['collectionSha256'], trust=self.trust, output=self.root / 'signed', dpapi=self.root / 'fake-test.dpapi')
        plaintext = bytearray(self.pem if key is None else key)
        with patch.dict(os.environ, {}, clear=True), patch.object(local, 'unprotect_dpapi', return_value=plaintext):
            result = local.sign_stage(args, now=NOW)
        self.assertEqual(set(plaintext), {0})
        self.signed = args.output
        return result

    def upload_args(self, apply=False):
        return SimpleNamespace(collection=self.directory, signatures=self.signed, trust=self.trust, apply=apply)

    def test_collect_uses_actual_promote_without_private_key_access(self):
        result = self.collect()
        self.assertFalse(result['privateKeyAccessed'])
        manifest, raw, files, snapshot, _, _ = local.check_collection(self.directory, self.trust, NOW, result['collectionSha256'])
        self.assertEqual(snapshot['entries'][0]['status'], 'published')
        self.assertEqual(manifest['files']['published.json']['sha256'], publish.digest(files['published.json']))

    def test_fresh_directories_cannot_overwrite_existing_data(self):
        self.collect()
        with self.assertRaises(FileExistsError):
            local.fresh(self.directory)
        self.assertTrue((self.directory / 'published.json').is_file())

    def test_sign_writes_public_envelopes_only(self):
        result = self.sign()
        self.assertFalse(result['privateKeyWritten'])
        self.assertEqual({p.name for p in self.signed.iterdir()}, local.SIGNATURE_FILES)
        for path in self.signed.iterdir():
            self.assertNotIn(b'PRIVATE KEY', path.read_bytes())
        local.upload_stage(self.upload_args(), now=NOW)

    def test_wrong_dpapi_key_refused_and_plaintext_cleared(self):
        self.collect()
        other = rsa.generate_private_key(public_exponent=65537, key_size=3072)
        plaintext = bytearray(other.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption()))
        args = SimpleNamespace(collection=self.directory, collection_sha256=self.collected['collectionSha256'], trust=self.trust, output=self.root / 'wrong-key', dpapi=self.root / 'fake.dpapi')
        with patch.dict(os.environ, {}, clear=True), patch.object(local, 'unprotect_dpapi', return_value=plaintext), self.assertRaises(MarketError):
            local.sign_stage(args, now=NOW)
        self.assertEqual(set(plaintext), {0})
        self.assertEqual(list(args.output.iterdir()), [])

    def test_sign_requires_reviewed_collection_hash_and_no_credentials(self):
        self.collect()
        args = SimpleNamespace(collection=self.directory, collection_sha256='0' * 64, trust=self.trust, output=self.root / 'never-written', dpapi=self.root / 'fake.dpapi')
        with patch.dict(os.environ, {}, clear=True), patch.object(local, 'unprotect_dpapi') as decrypt, self.assertRaises(MarketError):
            local.sign_stage(args, now=NOW)
        decrypt.assert_not_called()
        with patch.dict(os.environ, {'GITHUB_TOKEN': 'fake-token'}), self.assertRaises(MarketError):
            local.sign_stage(args, now=NOW)

    def test_duplicate_json_and_inventory_path_injection_refused(self):
        self.collect()
        path = self.directory / 'collection.json'
        original = path.read_bytes()
        path.write_bytes(original[:-2] + b',"kind":"duplicate"}\n')
        with self.assertRaises(MarketError):
            local.check_collection(self.directory, self.trust, NOW)
        path.write_bytes(original)
        (self.directory / '..-unexpected.json').write_bytes(b'{}')
        with self.assertRaises(MarketError):
            local.check_collection(self.directory, self.trust, NOW)

    def test_stale_and_future_collections_refused(self):
        self.collect()
        for now in ('2026-11-14T02:00:01Z', '2026-11-13T23:59:59Z'):
            with self.subTest(now=now), self.assertRaises(MarketError):
                local.check_collection(self.directory, self.trust, now)

    def test_modified_report_or_signed_manifest_refused(self):
        self.sign()
        report_path = self.directory / 'validation-reports.json'
        report_path.write_bytes(report_path.read_bytes() + b' ')
        with self.assertRaises(MarketError):
            local.upload_stage(self.upload_args(), now=NOW)
        manifest_path = self.directory / 'collection.json'
        manifest = json.loads(manifest_path.read_bytes())
        manifest['files']['validation-reports.json'] = {'size': report_path.stat().st_size, 'sha256': publish.digest(report_path.read_bytes())}
        manifest_path.write_bytes(canonical(manifest) + b'\n')
        with self.assertRaises(MarketError):
            local.upload_stage(self.upload_args(), now=NOW)

    def test_fixed_trust_and_signature_domains_are_independent(self):
        self.sign()
        envelope = json.loads((self.signed / 'published.sig.json').read_bytes())
        raw_manifest = (self.directory / 'collection.json').read_bytes()
        envelope['sha256'] = publish.digest(raw_manifest)
        with self.assertRaises(MarketError):
            local.verify_transfer(raw_manifest, envelope, self.key.public_key(), 'test')
        trusted = json.loads(self.trust.read_bytes()); trusted['repository'] = 'attacker/repo'; self.trust.write_bytes(canonical(trusted))
        with self.assertRaises(MarketError):
            local.upload_stage(self.upload_args(), now=NOW)

    def test_upload_dry_run_never_uses_network_or_writer(self):
        self.sign()
        writer = Mock()
        with patch.object(local, 'previous', side_effect=AssertionError('dry-run network')):
            result = local.upload_stage(self.upload_args(), writer=writer, now=NOW)
        self.assertFalse(result['applied'])
        writer.publish.assert_not_called()

    def test_upload_only_public_assets_and_rejects_advanced_audit(self):
        self.sign()
        writer = Mock()
        with patch.object(local, 'previous', return_value=(None, None)), patch.object(publish, 'verify_live_bindings'):
            result = local.upload_stage(self.upload_args(True), gh=self.gh, writer=writer, now=NOW)
        self.assertTrue(result['applied'])
        files = writer.publish.call_args.args[1]
        self.assertEqual(set(files), local.PUBLIC_FILES | local.SIGNATURE_FILES | {'collection.json'})
        self.assertFalse(any(b'PRIVATE KEY' in body for body in files.values()))
        snapshot = json.loads((self.directory / 'published.json').read_bytes()); snapshot['generation'] = 2
        with patch.object(local, 'previous', return_value=(snapshot, None)), self.assertRaises(MarketError):
            local.upload_stage(self.upload_args(True), gh=self.gh, writer=Mock(), now=NOW)

    def test_validator_child_never_inherits_api_or_signing_credentials(self):
        runtime = self.root / 'validator'; runtime.mkdir()
        names = ('NativeValidator.exe', 'NativeValidator.exe.config', 'Newtonsoft.Json.dll', 'native-platform-library.json')
        for name in names:
            (runtime / name).write_bytes(b'inert test fixture')
        digest = publish.digest(b'inert test fixture')
        admission = publish.NativeAdmission(runtime / names[0], digest, runtime / names[3], digest, {name: digest for name in names[:3]})
        result = SimpleNamespace(stdout=b'{"valid":true}', stderr=b'', returncode=0)
        with patch.dict(os.environ, {'GITHUB_TOKEN': 'fake-token', 'MARKET_SIGNING_PEM': 'fake-key', 'CUSTOM_PAT': 'fake-pat'}), patch.object(publish.subprocess, 'run', return_value=result) as run:
            admission(self.root / 'fake.spatial')
        self.assertTrue({'GITHUB_TOKEN', 'MARKET_SIGNING_PEM', 'CUSTOM_PAT'}.isdisjoint(run.call_args.kwargs['env']))


if __name__ == '__main__':
    unittest.main()
