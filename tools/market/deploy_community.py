"""Reviewable official community deployment; mutations require explicit --apply.

seal-secret runs as the Windows user who owns the DPAPI blob. It writes only a
GitHub libsodium sealed-box envelope, never a plaintext PEM. The administrator
can submit that envelope without access to the original user's DPAPI key.

API contracts: https://docs.github.com/en/rest/actions/secrets
https://docs.github.com/en/rest/actions/variables
https://docs.github.com/en/rest/deployments/environments
"""
import argparse
import base64
import ctypes
import hashlib
import json
import os
import re
import subprocess
import sys
import uuid
import zipfile
from pathlib import Path
from urllib.error import HTTPError
from urllib.parse import quote
from urllib.request import HTTPRedirectHandler, Request, build_opener

REPOSITORY = 'KyaKayKya/Spatial-Wallpaper-Community'
ENVIRONMENT = 'market-publish'
SECRET = 'MARKET_SIGNING_PEM'
MAX_JSON = 8 * 1024 * 1024


def need(value, reason):
    if not value:
        raise ValueError(reason)


def load(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def exclusive_json(path, value):
    path = Path(path)
    need(not path.exists(), 'refuse overwriting local output: ' + str(path))
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('x', encoding='utf-8', newline='\n') as output:
        json.dump(value, output, ensure_ascii=False, indent=2)
        output.write('\n')


def sha256(path):
    value = hashlib.sha256()
    with Path(path).open('rb') as source:
        for block in iter(lambda: source.read(1024 * 1024), b''):
            value.update(block)
    return value.hexdigest()


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, new_url):
        raise ValueError('GitHub API redirect refused')


def token_from_args(args):
    token = os.environ.get(args.token_env)
    if not token and args.git_credential:
        result = subprocess.run(['git', 'credential', 'fill'], input='protocol=https\nhost=github.com\n\n',
                                text=True, capture_output=True, check=True,
                                env=dict(os.environ, GIT_TERMINAL_PROMPT='0', GCM_INTERACTIVE='Never'))
        values = dict(line.split('=', 1) for line in result.stdout.splitlines() if '=' in line)
        need(values.get('host') == 'github.com' and values.get('protocol') == 'https', 'credential host mismatch')
        token = values.get('password')
    need(token, 'set token environment variable or explicitly choose --git-credential; token values are never printed')
    return token


class GitHub:
    def __init__(self, token, repository):
        self.token, self.repository = token, repository
        self.base = '/repos/' + repository

    def request(self, method, path, body=None, stream=None, length=None):
        need(path.startswith(self.base + '/') or path == self.base or method == 'GET' and path == '/user', 'API target outside selected community repository')
        headers = {'Accept': 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28',
                   'Authorization': 'Bearer ' + self.token, 'User-Agent': 'Spatial-Wallpaper-community-deploy/1'}
        origin = 'https://api.github.com'
        if stream is not None:
            origin = 'https://uploads.github.com'
            headers['Content-Type'] = 'application/octet-stream'
            headers['Content-Length'] = str(length)
            data = stream
        else:
            data = None if body is None else json.dumps(body, ensure_ascii=False).encode('utf-8')
            if data is not None:
                headers['Content-Type'] = 'application/json'
        try:
            with build_opener(NoRedirect()).open(Request(origin + path, data=data, headers=headers, method=method), timeout=120 if stream else 30) as response:
                raw = response.read(MAX_JSON + 1)
                need(len(raw) <= MAX_JSON, 'GitHub response budget exceeded')
                return json.loads(raw) if raw else None
        except HTTPError as failure:
            if method == 'GET' and failure.code == 404:
                return None
            # Do not dump HTTP bodies or request payloads: secret requests contain
            # encrypted data and administrative responses can include private context.
            raise RuntimeError('GitHub ' + method + ' HTTP ' + str(failure.code) + ': ' + path.split('?')[0]) from None

    def pages(self, path, field=None):
        result = []
        for page in range(1, 101):
            response = self.request('GET', path + ('&' if '?' in path else '?') + 'per_page=100&page=' + str(page))
            need(response is not None, 'GitHub list unavailable: ' + path)
            items = response[field] if field else response
            need(isinstance(items, list), 'GitHub list response expected')
            result.extend(items)
            if len(items) < 100:
                return result
        raise ValueError('GitHub pagination budget exceeded')


def environment_path(args):
    return '/repos/' + args.repository + '/environments/' + quote(args.environment, safe='')


def configure_plan(args):
    need(re.fullmatch(r'dev/v[0-9]+\.[0-9]+', args.branch), 'only explicit active dev/vX.Y deployment branch permitted')
    need(re.fullmatch(r'[A-Za-z0-9._-]{1,64}', args.key_id), 'invalid market key ID')
    need(re.fullmatch(r'[0-9a-f]{64}', args.validator_sha256), 'exact validator ZIP SHA256 required')
    return {'repository': args.repository, 'environment': args.environment, 'branch': args.branch,
            'reviewers': [{'type': 'User', 'id': value} for value in args.reviewer_id],
            'repositoryVariables': {'MARKET_KEY_ID': args.key_id, 'MARKET_VALIDATOR_SHA256': args.validator_sha256,
                                    'MARKET_PUBLICATION_ENABLED': 'true' if args.enable else 'false'},
            'changes': ['create/reconcile environment without removing existing reviewers',
                        'restrict new environment to the exact selected dev branch; never relax an existing protected-branch policy',
                        'set repository-scoped variables (PR check and job-level if must see them before environment secrets)',
                        'never change main protection or repository visibility']}


def configure(api, args):
    plan = configure_plan(args)
    endpoint = environment_path(args)
    previous = api.request('GET', endpoint)
    reviewers = list(plan['reviewers'])
    if previous:
        for rule in previous.get('protection_rules', []):
            if rule.get('type') == 'required_reviewers':
                for item in rule.get('reviewers', []):
                    reviewer = {'type': item['type'], 'id': item['reviewer']['id']}
                    if reviewer not in reviewers:
                        reviewers.append(reviewer)
    need(len(reviewers) <= 6, 'GitHub reviewer bound exceeded; existing reviewers are not removed')
    policy = previous.get('deployment_branch_policy') if previous else None
    body = {'deployment_branch_policy': policy if policy and policy.get('protected_branches') else {'protected_branches': False, 'custom_branch_policies': True}}
    if reviewers:
        body['reviewers'] = reviewers
    # Preserve existing environment wait/self-review/bypass policy exactly.
    if previous:
        for rule in previous.get('protection_rules', []):
            if rule.get('type') == 'wait_timer':
                body['wait_timer'] = rule['wait_timer']
            if rule.get('type') == 'required_reviewers':
                body['prevent_self_review'] = rule.get('prevent_self_review', False)
        if 'can_admins_bypass' in previous:
            body['can_admins_bypass'] = previous['can_admins_bypass']
    branch_policies = []
    if previous and not body['deployment_branch_policy'].get('protected_branches'):
        branch_policies = api.pages(endpoint + '/deployment-branch-policies', 'branch_policies')
        need(all(p['name'] == args.branch and p.get('type', 'branch') == 'branch' for p in branch_policies), 'existing deployment branch policies are broader/different; no environment changes applied')
    api.request('PUT', endpoint, body)
    if not body['deployment_branch_policy'].get('protected_branches'):
        need(all(p['name'] == args.branch and p.get('type', 'branch') == 'branch' for p in branch_policies), 'existing deployment branch policies are broader/different; review explicitly rather than replacing them')
        if not branch_policies:
            api.request('POST', endpoint + '/deployment-branch-policies', {'name': args.branch, 'type': 'branch'})
    variables = api.base + '/actions/variables'
    for name, value in plan['repositoryVariables'].items():
        existing = api.request('GET', variables + '/' + name)
        api.request('PATCH' if existing else 'POST', variables + '/' + name if existing else variables, {'name': name, 'value': value})
    return plan


class DataBlob(ctypes.Structure):
    _fields_ = [('size', ctypes.c_uint32), ('data', ctypes.POINTER(ctypes.c_ubyte))]


def unprotect_dpapi(path):
    need(os.name == 'nt', 'DPAPI secret sealing must run on the original Windows user account')
    encrypted = Path(path).read_bytes()
    need(0 < len(encrypted) <= 65536, 'DPAPI container size invalid')
    buffer = (ctypes.c_ubyte * len(encrypted)).from_buffer_copy(encrypted)
    source = DataBlob(len(encrypted), ctypes.cast(buffer, ctypes.POINTER(ctypes.c_ubyte)))
    output = DataBlob()
    crypt32 = ctypes.WinDLL('crypt32', use_last_error=True)
    kernel32 = ctypes.WinDLL('kernel32', use_last_error=True)
    crypt32.CryptUnprotectData.argtypes = [ctypes.POINTER(DataBlob), ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint32, ctypes.POINTER(DataBlob)]
    crypt32.CryptUnprotectData.restype = ctypes.c_int
    kernel32.LocalFree.argtypes = [ctypes.c_void_p]
    kernel32.LocalFree.restype = ctypes.c_void_p
    if not crypt32.CryptUnprotectData(ctypes.byref(source), None, None, None, None, 1, ctypes.byref(output)):
        raise OSError(ctypes.get_last_error(), 'DPAPI decrypt failed under this user; do not export a plaintext key to work around the account boundary')
    try:
        need(0 < output.size <= 65536, 'decrypted secret size invalid')
        return bytearray(ctypes.string_at(output.data, output.size))
    finally:
        if output.data:
            ctypes.memset(output.data, 0, output.size)
            kernel32.LocalFree(ctypes.cast(output.data, ctypes.c_void_p))


def seal_secret(args):
    # Probe crypto first: never decrypt if the required audited primitive is unavailable.
    try:
        from nacl.public import PublicKey, SealedBox
    except ImportError:
        raise RuntimeError('PyNaCl is required in this original-user environment; install it into a task-owned --target directory and set PYTHONPATH. No plaintext file fallback is supported.') from None
    public = load(args.public_key)
    need(set(public).issuperset({'key_id', 'key'}), 'expected GitHub environment public-key API JSON')
    key = base64.b64decode(public['key'], validate=True)
    need(len(key) == 32 and isinstance(public['key_id'], str) and public['key_id'], 'invalid GitHub sealed-box public key')
    if 'repository' in public:
        need(public['repository'] == args.repository and public['environment'] == args.environment, 'GitHub public key file target mismatch')
    need(not args.output.exists(), 'refuse overwriting sealed envelope')
    plaintext = unprotect_dpapi(args.dpapi)
    try:
        need(plaintext.startswith(b'-----BEGIN ') and b'PRIVATE KEY-----' in plaintext[:80], 'DPAPI payload is not the expected signing PEM')
        encrypted = SealedBox(PublicKey(key)).encrypt(bytes(plaintext))
    finally:
        for index in range(len(plaintext)):
            plaintext[index] = 0
    envelope = {'repository': args.repository, 'environment': args.environment, 'secret_name': SECRET,
                'key_id': public['key_id'], 'encrypted_value': base64.b64encode(encrypted).decode('ascii')}
    exclusive_json(args.output, envelope)
    return {'sealed': True, 'output': str(args.output), 'repository': args.repository, 'environment': args.environment,
            'plaintext_written': False, 'algorithm': 'libsodium crypto_box_seal (PyNaCl SealedBox)'}


def submit_secret(api, args):
    envelope = load(args.sealed)
    need(set(envelope) == {'repository', 'environment', 'secret_name', 'key_id', 'encrypted_value'}, 'unexpected sealed secret fields')
    need(envelope['repository'] == args.repository and envelope['environment'] == args.environment and envelope['secret_name'] == SECRET, 'sealed secret destination mismatch')
    encrypted = base64.b64decode(envelope['encrypted_value'], validate=True)
    need(48 < len(encrypted) < 65536, 'invalid sealed-box ciphertext size')
    current = api.request('GET', environment_path(args) + '/secrets/public-key')
    need(current and current['key_id'] == envelope['key_id'], 'GitHub environment public key changed; reseal under the original user')
    api.request('PUT', environment_path(args) + '/secrets/' + SECRET, {k: envelope[k] for k in ('key_id', 'encrypted_value')})
    return {'submitted_secret': SECRET, 'repository': args.repository, 'environment': args.environment, 'plaintext_available_to_admin': False}


def asset_plan(args):
    need(re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._+-]{0,99}', args.tag), 'unsafe Release tag')
    need(re.fullmatch(r'[0-9a-f]{40}', args.source_commit), 'exact public source commit required')
    names = [p.name for p in args.asset]
    need(len(set(names)) == len(names) and names, 'duplicate/empty Release asset set')
    assets = []
    for path in args.asset:
        need(path.is_file() and not path.is_symlink() and re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9._+-]{0,199}', path.name), 'invalid Release asset path/name')
        assets.append({'name': path.name, 'size': path.stat().st_size, 'sha256': sha256(path)})
    return {'repository': args.repository, 'tag': args.tag, 'sourceCommit': args.source_commit,
            'draftUntilComplete': True, 'publishAfterUpload': args.publish, 'makeLatest': False, 'assets': assets}


def upload_release(api, args):
    plan = asset_plan(args)
    endpoint = api.base + '/releases'
    existing_tag = api.request('GET', api.base + '/git/ref/tags/' + quote(args.tag, safe=''))
    existing_commit = api.request('GET', api.base + '/commits/' + quote(args.tag, safe='')) if existing_tag else None
    need(not existing_commit or existing_commit['sha'] == args.source_commit, 'existing tag points to a different source commit')
    source = api.request('GET', api.base + '/commits/' + args.source_commit)
    need(source and source['sha'] == args.source_commit, 'exact source commit is not present in the selected public repository')
    release = api.request('GET', endpoint + '/tags/' + quote(args.tag, safe=''))
    if not release:
        release = api.request('POST', endpoint, {'tag_name': args.tag, 'target_commitish': args.source_commit, 'name': args.tag,
                                                'draft': True, 'prerelease': False, 'make_latest': 'false',
                                                'body': 'Exact-source Spatial Wallpaper community release; see included file hashes and license notices.'})
    for path, expected in zip(args.asset, plan['assets']):
        existing = [a for a in release['assets'] if a['name'] == path.name]
        need(len(existing) <= 1, 'duplicate existing Release asset')
        if existing:
            server_digest = existing[0].get('digest')
            if server_digest == 'sha256:' + expected['sha256'] and existing[0]['size'] == expected['size']:
                continue
            need(release['draft'] and args.replace_draft, 'existing Release asset differs or lacks server digest; do not overwrite a published asset')
            api.request('DELETE', endpoint + '/assets/' + str(existing[0]['id']))
        with path.open('rb') as data:
            uploaded = api.request('POST', endpoint + '/' + str(release['id']) + '/assets?name=' + quote(path.name, safe=''), stream=data, length=expected['size'])
        need(uploaded['size'] == expected['size'] and uploaded.get('digest') == 'sha256:' + expected['sha256'], 'uploaded Release server digest mismatch')
        need(sha256(path) == expected['sha256'], 'local asset changed during upload')
    if args.publish and release['draft']:
        api.request('PATCH', endpoint + '/' + str(release['id']), {'draft': False, 'make_latest': 'false'})
    return plan


def works_from_packages(directory):
    works = []
    for path in sorted(Path(directory).glob('*.spatial')):
        with zipfile.ZipFile(path) as archive:
            matches = [i for i in archive.infolist() if i.filename == 'manifest.json']
            need(len(matches) == 1 and 0 < matches[0].file_size <= 1024 * 1024, 'bounded package manifest missing')
            manifest = json.loads(archive.read(matches[0]))
        work = str(uuid.UUID(manifest['work_id']))
        need(work == manifest['work_id'] and not any(w['workId'] == work for w in works), 'invalid/duplicate stable work GUID')
        works.append({'workId': work, 'title': manifest['name']})
    need(len(works) == 5, 'exactly five native works required for initial comments setup')
    return works


def comments_plan(args):
    need(args.author_repository_id > 0, 'stable author repository numeric ID required')
    return [{'key': str(args.author_repository_id) + ':' + work['workId'], 'workId': work['workId'], 'title': work['title'],
             'marker': '<!-- spatial-market:' + str(args.author_repository_id) + ':' + work['workId'] + ' -->',
             'label': 'work:' + work['workId']} for work in works_from_packages(args.packages)]


def comments(api, args):
    need(not args.output.exists(), 'refuse overwriting comments mapping before any API mutations')
    planned = comments_plan(args)
    actor = api.request('GET', '/user')
    issues = api.pages(api.base + '/issues?state=all')
    labels = {label['name'] for label in api.pages(api.base + '/labels')}
    mapping = {}
    for work in planned:
        matches = [issue for issue in issues if 'pull_request' not in issue and work['marker'] in (issue.get('body') or '').splitlines()
                   and issue['user']['id'] == actor['id']]
        need(len(matches) <= 1, 'duplicate authoritative work Issues; reconcile without creating another')
        if not matches:
            for label in ('wallpaper', work['label']):
                if label not in labels:
                    api.request('POST', api.base + '/labels', {'name': label, 'color': '2f81f7', 'description': 'Stable native wallpaper identity and community feedback'})
                    labels.add(label)
            issue = api.request('POST', api.base + '/issues', {'title': ('作品交流：' + work['title'])[:240],
                'body': work['marker'] + '\n\n作品 GUID：' + work['workId'] + '\n\n在此讨论操作、体验和后续修订。匿名可浏览，参与需 GitHub 登录。请勿公开个人数据或存档。\n\n下载次数不是安装人数；包准入不证明版权、画面、声音或 GPU 风险均已验收。举报请说明作品 GUID、修订和包 SHA256。',
                'labels': ['wallpaper', work['label']]})
        else:
            issue = matches[0]
        need(issue['html_url'] == 'https://github.com/' + args.repository + '/issues/' + str(issue['number']), 'unexpected created Issue URL')
        mapping[work['key']] = {'comments': {'kind': 'issues', 'url': issue['html_url'], 'number': issue['number']}}
    exclusive_json(args.output, mapping)
    return {'repository': args.repository, 'mapped_works': len(mapping), 'output': str(args.output)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', default=REPOSITORY)
    parser.add_argument('--environment', default=ENVIRONMENT)
    parser.add_argument('--token-env', default='GITHUB_TOKEN')
    parser.add_argument('--git-credential', action='store_true', help='Use current user git credential helper in memory, with interactive prompts disabled')
    commands = parser.add_subparsers(dest='command', required=True)
    configure_parser = commands.add_parser('configure', help='Show deployment changes; --apply writes environment and repository variables')
    configure_parser.add_argument('--branch', required=True)
    configure_parser.add_argument('--key-id', required=True)
    configure_parser.add_argument('--validator-sha256', required=True)
    configure_parser.add_argument('--reviewer-id', type=int, action='append', default=[])
    configure_parser.add_argument('--enable', action='store_true', help='Enable publisher only after prerequisites are actually deployed')
    configure_parser.add_argument('--apply', action='store_true')
    public = commands.add_parser('fetch-key', help='Read GitHub environment public key; never returns a secret')
    public.add_argument('--output', type=Path, required=True)
    seal = commands.add_parser('seal-secret', help='Local only: current-user DPAPI -> in-memory PyNaCl sealed box -> encrypted JSON')
    seal.add_argument('--dpapi', type=Path, required=True)
    seal.add_argument('--public-key', type=Path, required=True)
    seal.add_argument('--output', type=Path, required=True)
    secret = commands.add_parser('submit-secret', help='Submit only the sealed envelope; --apply required')
    secret.add_argument('--sealed', type=Path, required=True)
    secret.add_argument('--apply', action='store_true')
    release = commands.add_parser('upload-release', help='Review prepared local assets; --apply uploads, --publish ends draft')
    release.add_argument('--tag', required=True)
    release.add_argument('--source-commit', required=True)
    release.add_argument('--asset', type=Path, action='append', required=True)
    release.add_argument('--replace-draft', action='store_true')
    release.add_argument('--publish', action='store_true')
    release.add_argument('--apply', action='store_true')
    issue = commands.add_parser('comments', help='Review/create five stable work Issues; write details mapping, never user replies')
    issue.add_argument('--packages', type=Path, required=True)
    issue.add_argument('--author-repository-id', type=int, required=True)
    issue.add_argument('--output', type=Path, required=True)
    issue.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    need(re.fullmatch(r'[A-Za-z0-9-]+/[A-Za-z0-9_.-]+', args.repository), 'invalid repository')
    need(re.fullmatch(r'[A-Za-z0-9._-]{1,64}', args.environment), 'invalid environment')
    if args.command == 'seal-secret':
        result = seal_secret(args)
    elif args.command == 'configure' and not args.apply:
        result = dict(configure_plan(args), applied=False)
    elif args.command == 'upload-release' and not args.apply:
        result = dict(asset_plan(args), applied=False)
    elif args.command == 'comments' and not args.apply:
        result = {'repository': args.repository, 'planned_works': comments_plan(args), 'applied': False}
    elif args.command == 'submit-secret' and not args.apply:
        sealed = load(args.sealed)
        result = {'repository': sealed['repository'], 'environment': sealed['environment'], 'secret': sealed['secret_name'], 'key_id': sealed['key_id'], 'applied': False, 'plaintext_available': False}
    else:
        api = GitHub(token_from_args(args), args.repository)
        if args.command == 'fetch-key':
            key = api.request('GET', environment_path(args) + '/secrets/public-key')
            need(key is not None, 'create the environment before requesting its public key')
            exclusive_json(args.output, dict(key, repository=args.repository, environment=args.environment))
            result = {'read_only': True, 'output': str(args.output)}
        elif args.command == 'configure':
            result = configure(api, args)
        elif args.command == 'submit-secret':
            result = submit_secret(api, args)
        elif args.command == 'upload-release':
            result = upload_release(api, args)
        else:
            result = comments(api, args)
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
