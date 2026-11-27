#!/usr/bin/env python3
"""Turns a browser HAR recording (DevTools → Network → "Save all as HAR") of a bastion portal into a summary that keeps
the shape of the conversation and none of its secrets: the order of the requests, methods, paths, parameter and field
names, status codes, cookie names and the structure of JSON answers. Every value that could be a password, a one-time
code, a cookie, a token, a ticket, an address, a user or an asset name is replaced by its kind and length.

Local only: reads one file, writes one file, opens no connection.

  har-summary.py RECORDING.har SUMMARY.txt
"""
import base64, json, re, sys, urllib.parse
from collections import Counter

SENSITIVE = re.compile(r'pass|pwd|secret|token|otp|totp|mfa|code|captcha|ticket|cookie|session|sess|auth|key|sign|nonce|salt|cred|jwt|csrf|xsrf|verify|sms|phone|mobile|mail|user|name|account|login|host|ip|addr|url|uri|path|redirect|callback', re.I)
ENUMISH = re.compile(r'^[a-z_]{1,12}$')
IPV4 = re.compile(r'(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?![\d.])')
HOSTISH = re.compile(r'(?<![\w.-])(?:[A-Za-z0-9-]{1,63}\.){1,10}[A-Za-z]{2,63}(?![\w-])')
LONG = re.compile(r'[A-Za-z0-9+/=_-]{16,}')
STATIC = re.compile(r'\.(?:js|mjs|css|png|jpe?g|gif|svg|ico|woff2?|ttf|eot|map|webp)(?:$|\?)', re.I)

hosts = {}
def host_name(netloc):
    if netloc not in hosts: hosts[netloc] = '‹门户›' if not hosts else f'‹其他主机{len(hosts)}›'
    return hosts[netloc]

def kind(value):
    """What a value is, never what it says."""
    if value is None: return 'null'
    if isinstance(value, bool): return 'true' if value else 'false'
    if isinstance(value, (int, float)): return '数字'
    s = str(value)
    if s == '': return '空'
    if re.fullmatch(r'-?\d{1,18}', s): return f'数字文本({len(s)})'
    if re.fullmatch(r'[0-9a-fA-F-]{32,36}', s): return '标识'
    if s.count('.') == 2 and all(re.fullmatch(r'[A-Za-z0-9_-]+', p) for p in s.split('.')) and len(s) > 40: return f'JWT({len(s)})'
    if '://' in s: return '网址(' + url_shape(s) + ')'
    return f'文本({len(s)})'

def text_value(key, value):
    """Short lower-case words under harmless keys (protocol: ssh, status: ok) stay; everything else is its kind."""
    if isinstance(value, str) and ENUMISH.match(value) and not SENSITIVE.search(key or ''): return value
    if isinstance(value, bool) or value is None: return kind(value)
    if isinstance(value, (int, float)) and not SENSITIVE.search(key or '') and abs(value) < 1000 and float(value).is_integer(): return str(int(value))
    return kind(value)

def field(name):
    """A field or parameter name: kept (marked, so that the last guard does not take it for a host name), unless it
    holds numbers long enough to be somebody's."""
    name = re.sub(r'\d{6,}', '{数字}', str(name))[:80]
    return '⟦' + name.replace('⟦', '').replace('⟧', '') + '⟧'

def segment(seg):
    if seg == '': return seg
    s = urllib.parse.unquote(seg)
    if re.fullmatch(r'\d+', s): return '{数字}'
    if re.fullmatch(r'[0-9a-fA-F-]{32,36}', s): return '{标识}'
    if IPV4.search(s) or HOSTISH.fullmatch(s): return '{地址}'
    if LONG.fullmatch(s) and re.search(r'\d', s): return '{令牌}'
    if len(s) > 40: return '{文本}'
    return s

def url_shape(url):
    try: u = urllib.parse.urlsplit(url)
    except ValueError: return '无法解析'
    if u.scheme not in ('http', 'https', 'ws', 'wss'):
        # A launch address such as ssh://ticket:password@host:port: only its form.
        user = '‹票据›:‹密码›@' if u.password is not None else ('‹用户›@' if u.username else '')
        try: port = ':‹端口›' if u.port else ''
        except ValueError: port = ':‹端口›'
        rest = '/'.join(segment(x) for x in u.path.split('/')) if u.path not in ('', '/') else ''
        query = urllib.parse.parse_qsl(u.query, keep_blank_values=True)
        q = '&'.join(f'{field(k)}={text_value(k, v)}' for k, v in query)
        return f'{u.scheme}://{user}' + ('‹地址›' if u.hostname else '') + port + rest + (f'?{q}' if q else '')
    path = '/'.join(segment(s) for s in u.path.split('/'))
    query = urllib.parse.parse_qsl(u.query, keep_blank_values=True)
    q = '&'.join(f'{field(k)}={text_value(k, v)}' for k, v in query)
    return f'{u.scheme}://{host_name(u.netloc)}{path}' + (f'?{q}' if q else '')

def shape(data, key='', depth=0):
    if depth > 8: return '…'
    if isinstance(data, dict):
        return {field(k): shape(v, k, depth + 1) for k, v in list(data.items())[:60]} | ({'…': f'另有 {len(data) - 60} 项'} if len(data) > 60 else {})
    if isinstance(data, list):
        if not data: return []
        return [shape(data[0], key, depth + 1)] + ([f'…共 {len(data)} 项'] if len(data) > 1 else [])
    return text_value(key, data)

def body_shape(text, mime):
    if text is None or text == '': return None
    mime = (mime or '').lower()
    if 'json' in mime or text.lstrip()[:1] in ('{', '['):
        try: return json.dumps(shape(json.loads(text)), ensure_ascii=False)
        except ValueError: pass
    if 'x-www-form-urlencoded' in mime:
        return '表单 ' + ', '.join(f'{field(k)}={text_value(k, v)}' for k, v in urllib.parse.parse_qsl(text, keep_blank_values=True))
    if 'html' in mime:
        forms = re.findall(r'<form[^>]*action=["\']([^"\']*)', text, re.I)
        inputs = re.findall(r'<input[^>]*name=["\']([^"\']+)', text, re.I)
        launch = sorted({m.split(':', 1)[0] for m in re.findall(r'\b(?:xshell|xftp|ssh|sftp|ftp|rdp|vnc|telnet)[a-z]*://[^\s"\'<>]+', text, re.I)})
        return f'HTML {len(text)} 字' + (f'；表单 {len(forms)} 个，输入项 {", ".join(sorted(set(inputs))[:30])}' if forms or inputs else '') + (f'；含启动地址 {", ".join(launch)}' if launch else '')
    found = sorted({m.split(':', 1)[0] for m in re.findall(r'\b(?:xshell|xftp|ssh|sftp|ftp|rdp|vnc|telnet)[a-z]*://[^\s"\'<>]+', text, re.I)})
    return f'{mime or "未知类型"} {len(text)} 字' + (f'；含启动地址 {", ".join(found)}' if found else '')

def cookie_name(name):
    """Some portals put user or session numbers into the names of their cookies."""
    return LONG.sub(lambda m: '{编号}' if re.search(r'\d', m.group(0)) else m.group(0), re.sub(r'\d{6,}', '{数字}', name))

def response_text(content):
    text = content.get('text')
    if text is not None and content.get('encoding') == 'base64':
        try: text = base64.b64decode(text).decode('utf-8', 'replace')
        except Exception: return None
    return text

def main(source, target):
    har = json.load(open(source, encoding='utf-8'))
    entries = sorted(har['log']['entries'], key=lambda e: e.get('startedDateTime', ''))
    start = entries[0]['startedDateTime'] if entries else ''
    from datetime import datetime
    def at(e):
        try: return int((datetime.fromisoformat(e['startedDateTime'].replace('Z', '+00:00')) - datetime.fromisoformat(start.replace('Z', '+00:00'))).total_seconds() * 1000)
        except Exception: return 0
    out, skipped = [], Counter()
    for e in entries:
        req, res = e['request'], e['response']
        if STATIC.search(req['url']) and res.get('status') in (200, 304): skipped[host_name(urllib.parse.urlsplit(req['url']).netloc)] += 1; continue
        lines = [f'{at(e):>8} ms  {req["method"]} {url_shape(req["url"])}  →  {res.get("status")}']
        notable = [h['name'] for h in req.get('headers', []) if 'token' in h['name'].lower() or h['name'].lower() in ('authorization', 'x-requested-with', 'x-jms-org')]
        if notable: lines.append('           请求头（只列名字）：' + ', '.join(sorted(set(notable))))
        if req.get('cookies'): lines.append('           带的 Cookie：' + ', '.join(sorted({cookie_name(c['name']) for c in req['cookies']})))
        if (post := req.get('postData')) is not None:
            b = body_shape(post.get('text'), post.get('mimeType'))
            if b is None and post.get('params'): b = '表单 ' + ', '.join(f'{field(p["name"])}={text_value(p["name"], p.get("value", ""))}' for p in post['params'])
            if b: lines.append('           请求体：' + b)
        cookies = [cookie_name(c['name']) + ('（HttpOnly）' if c.get('httpOnly') else '') for c in res.get('cookies', [])]
        if cookies: lines.append('           设置 Cookie：' + ', '.join(cookies))
        tokens = [h['name'] for h in res.get('headers', []) if 'token' in h['name'].lower()]
        if tokens: lines.append('           返回头（只列名字）：' + ', '.join(sorted(set(tokens))))
        location = next((h['value'] for h in res.get('headers', []) if h['name'].lower() == 'location'), None)
        if location: lines.append('           跳转到：' + url_shape(urllib.parse.urljoin(req['url'], location)))
        b = body_shape(response_text(res.get('content', {})), res.get('content', {}).get('mimeType'))
        if b: lines.append('           返回：' + b)
        for m in e.get('_webSocketMessages', [])[:40]:
            lines.append(f'           WebSocket {"→" if m.get("type") == "send" else "←"} ' + (body_shape(m.get('data'), 'application/json') or ''))
        out.append('\n'.join(lines))
    text = '\n'.join(out)
    # Last guard: nothing that looks like an address or a long token leaves.
    text = IPV4.sub('‹地址›', text)
    text = re.sub(r'(⟦[^⟧]*⟧)|' + HOSTISH.pattern, lambda m: m.group(1) or (m.group(0) if m.group(0).lower().endswith(('.js', '.css', '.html')) else '‹主机名›'), text)
    text = text.replace('⟦', '').replace('⟧', '')
    summary = f'请求 {len(out)} 个（另有静态文件 {sum(skipped.values())} 个，没有列出）\n' + text + '\n'
    open(target, 'w', encoding='utf-8').write(summary)
    print(f'已写入 {target}：{len(out)} 个请求')

if __name__ == '__main__':
    if len(sys.argv) != 3: print(__doc__); sys.exit(2)
    main(sys.argv[1], sys.argv[2])
