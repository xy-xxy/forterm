#!/usr/bin/env python3
"""A stand-in for a Sangfor-style "fort" bastion portal, for Studio's tests: 127.0.0.1 only, a certificate made at start,
synthetic users and resources. It answers the calls the real portal's pages make (recorded 2026-11-27, see
docs/STUDIO-ALPHA13-DELIVERY.md) and checks them as strictly as it can: the rd1/rd2 signature of every call, the SM3 of
the password, the app code, the cookies of the session, the access token after login and the rotating action token.

Prints one JSON line {"port", "fingerprint"} and then answers commands on stdin: "stats", "stop".
Environment: FORT_SSH_PORT (what get_sshd_port answers), FORT_SID (the sid it hands out), FORT_FAIL (a failure to show:
"password", "tenant", "method", "change-password", "no-sso", "session-warn" (isWarn "msg"), "admin-ip" (fortAdminIp in the
login answer), "no-pass" (is_no_pass true for the preset accounts, whose password then has to be sent)).
As the recorded portal does, the session cookie changes with the login; calls with the old one are sent to the login page.
"""
import base64, datetime, hashlib, hmac, http.server, json, os, secrets, ssl, struct, sys, tempfile, threading, time, urllib.parse
from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.x509.oid import NameOID

TENANT, USER, PASSWORD = 'synthetic-tenant', 'synthetic-user', 'synthetic-password'
SEED = base64.b32decode('JBSWY3DPEHPK3PXP')  # the authenticator seed of the synthetic user
TARGET_USER, TARGET_PASSWORD = 'root', 'synthetic-target'
FAIL = os.getenv('FORT_FAIL', '')
SID = os.getenv('FORT_SID', '0f8fad5b-d9cb-469f-a165-70867728950e')
lock = threading.Lock()
stats = {'calls': [], 'unsigned': [], 'badSignature': [], 'badToken': [], 'logins': 0, 'logouts': 0, 'registered': [], 'targets': [], 'userAgents': [], 'noPass': []}
session = {'cookie': None, 'logged': False, 'access': None, 'action': None}
ASSETS = [
    {'fortResourceId': 'res%021d' % i, 'fortResourceName': f'synthetic-{i:02d}', 'fortResourceIp': f'192.0.2.{i}', 'fortResourceIps': '',
     'fortResourceTypeName': 'Linux', 'fortResourceTypeId': '1000000000076', 'fortAccounts': f'acc{i:021d}:root|usr{i:021d}:$user',
     'fortProtocolClient': None, 'fortOperationsProtocol': None, 'fortResourceState': '1', 'fortTargetCode': '22'}
    for i in range(1, 24)]

# The portal's own script (myajax.js): two digits → four characters, then three moves.
ROWS = ['ab', 'ef', 'cd', 'gh', 'kl', 'ij', 'mn', 'qr', 'op', 'st']
UP = [True, True, False, False, False, True, True, True, False, False]
PAIRS = {f'{t}{o}': f'{ROWS[t][0]}{9 - o}{ROWS[t][1]}{o if UP[t] else 9 - o}' for t in range(10) for o in range(10)}
KEY = '02270171'
def unsign(text):
    if len(text) != 32: return None
    r = text[0:7] + text[31] + text[8:31] + text[7]
    r = r[0:7] + r[15:18] + r[10:15] + r[7:10] + r[18:]
    r = r[28:31] + r[3:28] + r[0:3] + r[31:]
    back = {v: k for k, v in PAIRS.items()}
    digits = ''
    for i in range(8):
        pair = back.get(r[i * 4:i * 4 + 4])
        if pair is None or pair[1] != KEY[i]: return None
        digits += pair[0]
    return digits

def sm3(text):
    h = hashes.Hash(hashes.SM3()); h.update(text.encode()); return h.finalize().hex()

def decode64(text):
    """The inverse of the portal's encode64 (reversed; every third followed by the one before; base64)."""
    e = text.replace('%2B', '+')[::-1]
    out, i = '', 0
    position = 0
    while i < len(e):
        out += e[i]
        if position % 3 == 0 and position > 0: i += 1  # skip the repeated one
        i += 1; position += 1
    return base64.b64decode(out).decode()

def totp(at):
    counter = struct.pack('>Q', int(at // 30))
    digest = hmac.new(SEED, counter, hashlib.sha1).digest()
    o = digest[-1] & 15
    return '%06d' % ((struct.unpack('>I', digest[o:o + 4])[0] & 0x7fffffff) % 1000000)

def certificate():
    key = ec.generate_private_key(ec.SECP256R1())
    name = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, 'synthetic fort portal')])
    now = datetime.datetime.now(datetime.timezone.utc)
    cert = (x509.CertificateBuilder().subject_name(name).issuer_name(name).public_key(key.public_key()).serial_number(x509.random_serial_number())
            .not_valid_before(now - datetime.timedelta(days=1)).not_valid_after(now + datetime.timedelta(days=30))
            .add_extension(x509.SubjectAlternativeName([x509.IPAddress(__import__('ipaddress').ip_address('127.0.0.1'))]), False).sign(key, hashes.SHA256()))
    folder = tempfile.mkdtemp(prefix='fort-fixture-')
    with open(os.path.join(folder, 'cert.pem'), 'wb') as f: f.write(cert.public_bytes(serialization.Encoding.PEM))
    with open(os.path.join(folder, 'key.pem'), 'wb') as f: f.write(key.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption()))
    return folder, hashlib.sha256(cert.public_bytes(serialization.Encoding.DER)).hexdigest()

class Portal(http.server.BaseHTTPRequestHandler):
    protocol_version = 'HTTP/1.1'
    def log_message(self, *a): pass

    def send(self, status, body, kind='application/json;charset=UTF-8', headers=None):
        data = body.encode() if isinstance(body, str) else body
        self.send_response(status)
        self.send_header('Content-Type', kind); self.send_header('Content-Length', str(len(data)))
        for k, v in (headers or {}).items(): self.send_header(k, v)
        self.end_headers(); self.wfile.write(data)

    def json(self, obj, headers=None): self.send(200, json.dumps(obj, ensure_ascii=False), headers=headers)

    def cookie(self):
        jar = {}
        for part in (self.headers.get('Cookie') or '').split(';'):
            if '=' in part: k, v = part.strip().split('=', 1); jar[k] = v
        return jar.get('FORTSESSIONID')

    def signed(self, query):
        rd1, rd2 = query.get('rd1', [''])[0], query.get('rd2', [''])[0]
        d1, d2 = unsign(rd1), unsign(rd2)
        if d1 is None or d2 is None: return False
        now = str(int(time.time() * 1000))[2:10]
        return abs(int(d1) - int(now)) <= 60  # digits 3..10 of the time in ms are its seconds: within a minute

    def do_GET(self):
        u = urllib.parse.urlsplit(self.path); q = urllib.parse.parse_qs(u.query, keep_blank_values=True)
        with lock: stats['calls'].append('GET ' + u.path); stats['userAgents'].append(self.headers.get('User-Agent', ''))
        if u.path == '/fort/login':
            sid = secrets.token_hex(16)
            with lock: session.update(cookie=sid, logged=False, access=None, action=None)
            return self.send(200, '<html><body><form><input type="hidden" id="r" value="4711"/><input id="tenantName"/></form></body></html>', 'text/html;charset=UTF-8',
                             {'Set-Cookie': f'FORTSESSIONID={sid}; Path=/fort; HttpOnly'})
        if self.cookie() != session['cookie']: return self.send(302, '', headers={'Location': '/fort/login'})
        if u.path == '/fort/': return self.send(200, '<html><frameset></frameset></html>', 'text/html;charset=UTF-8')
        if u.path == '/fort/operation/sso/sso':
            if not self.signed(q): return self.bad_signature(u.path)
            if not session['logged']: return self.send(302, '', headers={'Location': '/fort/login'})
            return self.send(200, '<html><script>var fort_ip = "127.0.0.1";</script></html>', 'text/html;charset=UTF-8')
        self.send(404, '<html>not here</html>', 'text/html')

    def bad_signature(self, path):
        with lock: stats['badSignature'].append(path)
        return self.json({'errorMessage': '请求校验失败'})

    def do_POST(self):
        u = urllib.parse.urlsplit(self.path); q = urllib.parse.parse_qs(u.query, keep_blank_values=True)
        length = int(self.headers.get('Content-Length') or 0); raw = self.rfile.read(length).decode()
        form = {k: v[0] for k, v in urllib.parse.parse_qs(raw, keep_blank_values=True).items()}
        path = u.path[len('/fort/'):] if u.path.startswith('/fort/') else u.path
        with lock: stats['calls'].append('POST ' + path)
        if self.cookie() != session['cookie'] or session['cookie'] is None: return self.send(302, '', headers={'Location': '/fort/login'})
        if path == 'login/abc':
            ok = len(form.get('abc', '')) == 8 and form.get('r') == '4711' and base64.b64decode(form.get('haref', '')).decode().endswith('/fort/')
            if 'rd1' in q: ok = False
            return self.json({} if ok else {'errorMessage': 'abc'})
        if not self.signed(q): return self.bad_signature(path)
        if self.headers.get('X-Requested-With') != 'XMLHttpRequest':
            with lock: stats['unsigned'].append(path)
        if session['logged'] and path not in ('login/do_logout',) and self.headers.get('access_token') != session['access']:
            with lock: stats['badToken'].append(path)
            return self.json({'errorMessage': '令牌无效'})
        handler = getattr(self, 'p_' + path.replace('/', '_'), None)
        if handler is None: return self.json({'errorMessage': '没有这个接口'})
        return handler(form, q, raw)

    # ---- login ----
    def p_login_login_tenant(self, form, q, raw):
        if FAIL == 'tenant' or form.get('fortTenantName') != TENANT: return self.json({'checkTenantName': '0'})
        return self.json({'checkTenantName': '1', 'fortTenantName': TENANT, 'fortTenantId': 't' * 24, 'fortTenantIdReal': 'r' * 24})
    def p_login_get_auth(self, form, q, raw):
        methods = ['2-用户名+密码(默认方式)'] + ([] if FAIL == 'method' else ['34-用户名+密码+手机APP动态口令'])
        return self.json({'contentsAuths': methods if form.get('fortTenantId') == 'r' * 24 else []})
    def p_login_get_code(self, form, q, raw): return self.json({'success': 'success'})
    def p_login_do_login(self, form, q, raw):
        # Sent as the page puts it together: not encoded.
        fields = dict(p.split('=', 1) for p in raw.split('&') if '=' in p)
        with lock: stats['logins'] += 1
        code_ok = decode64(fields.get('cellPhonePwd', '')) in (totp(time.time()), totp(time.time() - 30))
        if (fields.get('loginMethod') != '34' or fields.get('username') != USER or fields.get('pwd') != sm3(PASSWORD) or not code_ok
                or fields.get('fortTenantIdReal') != 'r' * 24 or FAIL == 'password'):
            return self.json({'success': 'success', 'errorList': [{'error': '用户名或密码错误，还可以尝试4次'}]})
        rotated = secrets.token_hex(16)
        with lock: session.update(logged=True, access='access-1', action='action-1', cookie=rotated)
        answer = {'fortPasswordStrategy': {'fortPeriod': '90'}, 'errorList': [], 'success': 'success', 'isAdmin': '0', 'actionToken': 'action-1',
                  'fortForbiddenCharacter': [], 'fortPeriod': '0', 'accessToken': 'access-1'}
        if FAIL == 'change-password': answer['fortPeriod'] = '2'
        if FAIL == 'admin-ip': answer['fortAdminIp'] = '不允许从这个地址登录'
        return self.json(answer, headers={'Set-Cookie': f'FORTSESSIONID={rotated}; Path=/fort; HttpOnly'})
    def p_login_isWarn(self, form, q, raw):
        with lock: stats['calls'].append('isWarn:' + ('answer' if '"success"' in raw and '"accessToken"' in raw else 'other'))
        return self.json({'isWarn': 'msg'} if FAIL == 'session-warn' else {'isWarn': ''})
    def p_login_setReload2(self, form, q, raw): return self.json({'success': 'success'})
    def p_login_search_login(self, form, q, raw):
        if not session['logged']: return self.json({'success': 'success', 'user': ''})
        with lock: session.update(access='access-2', action='action-2')
        return self.json({'success': 'success', 'user': 'logining', 'accessToken': 'access-2', 'actionToken': 'action-2', 'changePwdFlag': None})
    def p_login_do_logout(self, form, q, raw):
        with lock: session.update(logged=False, access=None, action=None); stats['logouts'] += 1
        return self.json({'success': 'success'})

    # ---- resources ----
    def need(self):
        if not session['logged']: self.send(302, '', headers={'Location': '/fort/login'}); return False
        return True
    def p_sso_get_sshd_port(self, form, q, raw):
        if not self.need(): return
        return self.json({'sshd_port': os.getenv('FORT_SSH_PORT', '2222'), 'success': 'success'})
    def p_sso_list_department_tree(self, form, q, raw):
        if not self.need(): return
        return self.json({'fortTree': [{'id': 'd' * 24, 'name': 'synthetic'}]})
    def p_sso_list_sso(self, form, q, raw):
        if not self.need(): return
        if q.get('fortSsoQuery.fortDepartmentIds', [''])[0] != 'd' * 24: return self.json({'errorMessage': '部门'})
        needle = q.get('fortSsoQuery.fortResourceNameOrIp', [''])[0]
        rows = [a for a in ASSETS if needle in a['fortResourceName'] or needle in a['fortResourceIp']]
        index, size = int(form.get('pagination.pageIndex', '1')), int(form.get('pagination.pageSize', '10'))
        page = rows[(index - 1) * size:index * size]
        return self.json({'msg': '1', 'pagination': {'pageSize': str(size), 'pageIndex': str(index), 'recondSum': str(len(rows)),
                          'pageSum': str(max(1, -(-len(rows) // size))), 'list': page, 'hasPageUp': str(index > 1).lower(), 'hasPageDown': str(index * size < len(rows)).lower(), 'offSite': '0'}, 'success': 'success'})
    def p_sso_get_operator_client_by_resource(self, form, q, raw):
        if not self.need(): return
        return self.json({'success': 'success', 'client': [{'fortProtocolClient': '1000000000002:Xshell|1000000000032:Xftp|1000000000005:putty',
                                                             'fortOperationsProtocol': 'protoco:1000000000002:SSH2|protoco:1000000000005:SFTP'}], 'ssoUrgent': None, 'ssoAuditEmergency': '0'})
    def sso(self, raw, files):
        if not self.need(): return
        fields = dict(p.split('=', 1) for p in raw.split('&') if '=' in p)
        if FAIL == 'no-sso': return self.json({'errorMessage': '没有可用的授权'})
        resource = next((a for a in ASSETS if a['fortResourceId'] == fields.get('fortSsoVoQuery.fortResoureceId')), None)
        if resource is None or fields.get('fortSsoVoQuery.fortIp') != '127.0.0.1' or fields.get('selectResourceIp') != resource['fortResourceIp']:
            return self.json({'errorMessage': '资源不对'})
        if files and (fields.get('fortSsoVoQuery.fortOperationsProtocolId') != '1000000000005' or fields.get('fortSsoVoQuery.fortClientToolId') != '1000000000032'):
            return self.json({'errorMessage': 'sftp'})
        if not files and (fields.get('fortSsoVoQuery.fortOperationsProtocolId') != '1000000000002' or fields.get('fortSsoVoQuery.clientId') != '1000000000002'):
            return self.json({'errorMessage': 'ssh'})
        account = fields.get('fortSsoVoQuery.fortAccountId', '')
        if account.startswith('usr') or FAIL == 'no-pass':
            password = decode64(urllib.parse.unquote(urllib.parse.unquote(fields.get('ssoPwd', ''))))
            if fields.get('ssoAccount') != TARGET_USER or password != TARGET_PASSWORD: return self.json({'errorMessage': '目标账号或密码错误'})
        elif fields.get('ssoAccount') != 'undefined' or fields.get('ssoPwd') != 'undefined':
            # What the page's getSsoCmdData/getSsoFtpData send when called with "" (func[0] is undefined).
            return self.json({'errorMessage': '代填参数不对'})
        with lock: stats['targets'].append({'resource': resource['fortResourceName'], 'account': account, 'files': files})
        return self.json({'fortSsoVoQuery': {'sid': SID, 'fortAccountId': account, 'fortResourceIp': resource['fortResourceIp'], 'fortAccountPassword': ''}, 'success': 'success'})
    def p_sso_is_no_pass(self, form, q, raw):
        if not self.need(): return
        if form.get('fortIp') != '127.0.0.1' or not form.get('accountId', '').startswith('acc'): return self.json({'errorMessage': 'is_no_pass'})
        with lock: stats['noPass'].append(form['accountId'])
        return self.json({'isNoPass': FAIL == 'no-pass', 'userLoginMethod': '0'})
    def p_sso_get_sso_cmd(self, form, q, raw): return self.sso(raw, False)
    def p_sso_get_sso_ftp(self, form, q, raw): return self.sso(raw, True)
    def p_process_set_double_approval_sid(self, form, q, raw):
        if not self.need(): return
        if self.headers.get('action_token') != session['action']:
            with lock: stats['badToken'].append('action_token')
            return self.json({'errorMessage': '操作令牌无效'})
        with lock: stats['registered'].append(form.get('sid')); session['action'] = 'action-%d' % (len(stats['registered']) + 2)
        return self.json({'success': 'success'}, headers={'action_token': session['action']})

def main():
    folder, fingerprint = certificate()
    server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Portal)
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER); context.load_cert_chain(os.path.join(folder, 'cert.pem'), os.path.join(folder, 'key.pem'))
    server.socket = context.wrap_socket(server.socket, server_side=True)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    print(json.dumps({'port': server.server_address[1], 'fingerprint': fingerprint, 'seed': base64.b32encode(SEED).decode()}), flush=True)
    for line in sys.stdin:
        command = line.strip()
        if command == 'stats':
            with lock: print(json.dumps(stats, ensure_ascii=False), flush=True)
        elif command == 'stop': break
    server.shutdown()

if __name__ == '__main__': main()
