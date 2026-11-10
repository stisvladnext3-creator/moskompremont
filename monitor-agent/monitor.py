#!/usr/bin/env python3
"""Read a private 3x-ui URI subscription and probe each outbound using an isolated Xray client."""
from __future__ import annotations
import argparse, base64, concurrent.futures, datetime, gzip, hashlib, json, logging, math, os, re, signal, socket, subprocess, tempfile, threading, time, urllib.error, urllib.parse, urllib.request, uuid
from pathlib import Path

LOG = logging.getLogger('moskom-monitor')
STOP = threading.Event()
TARGETS = ['https://www.gstatic.com/generate_204', 'https://cp.cloudflare.com/generate_204']
LIMIT = 2 * 1024 * 1024

def stamp():
    return datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%d %H:%M:%S')

class Unsupported(ValueError):
    pass

def decode64(value):
    value = re.sub(r'\s+', '', value).replace('-', '+').replace('_', '/')
    return base64.b64decode(value + '=' * (-len(value) % 4), validate=True).decode('utf-8-sig')

def integer(value, low=1, high=65535):
    number = int(value)
    if not low <= number <= high:
        raise Unsupported('number')
    return number

def transport(q, address, default_tls=False):
    network = q.get('type', 'tcp') or 'tcp'
    if network not in ('tcp', 'raw', 'ws', 'grpc', 'httpupgrade', 'xhttp'):
        raise Unsupported('transport')
    security = q.get('security', 'tls' if default_tls else 'none') or 'none'
    stream = {'network': network, 'security': security}
    if security == 'tls':
        if q.get('allowInsecure', '0').lower() in ('1', 'true'):
            raise Unsupported('insecure TLS')
        tls = {'serverName': q.get('sni') or q.get('serverName') or address, 'allowInsecure': False}
        if q.get('fp'): tls['fingerprint'] = q['fp']
        if q.get('alpn'): tls['alpn'] = q['alpn'].split(',')
        stream['tlsSettings'] = tls
    elif security == 'reality':
        if not q.get('pbk') or not (q.get('sni') or q.get('serverName')):
            raise Unsupported('reality')
        stream['realitySettings'] = {'serverName': q.get('sni') or q['serverName'], 'fingerprint': q.get('fp') or 'chrome', 'publicKey': q['pbk'], 'shortId': q.get('sid', ''), 'spiderX': q.get('spx', '/')}
    elif security != 'none':
        raise Unsupported('security')
    if network == 'ws':
        settings = {'path': q.get('path') or '/'}
        if q.get('host'): settings['headers'] = {'Host': q['host']}
        if q.get('ed'): settings.update(maxEarlyData=integer(q['ed'], 0), earlyDataHeaderName=q.get('eh') or 'Sec-WebSocket-Protocol')
        stream['wsSettings'] = settings
    elif network == 'grpc':
        settings = {'serviceName': q.get('serviceName') or q.get('path') or '', 'multiMode': q.get('mode') == 'multi'}
        if q.get('authority'): settings['authority'] = q['authority']
        stream['grpcSettings'] = settings
    elif network in ('httpupgrade', 'xhttp'):
        if q.get('extra') and q['extra'] != '{}': raise Unsupported('extra transport settings')
        settings = {'path': q.get('path') or '/', 'host': q.get('host', '')}
        if network == 'xhttp': settings['mode'] = q.get('mode') or 'auto'
        stream[network + 'Settings'] = settings
    elif q.get('headerType', 'none') == 'http':
        stream['tcpSettings'] = {'header': {'type': 'http', 'request': {'path': [q.get('path') or '/'], 'headers': {'Host': (q.get('host') or address).split(',')}}}}
    elif q.get('headerType', 'none') not in ('none', ''):
        raise Unsupported('header')
    return stream

def uri_outbound(uri):
    scheme = uri.split(':', 1)[0].lower()
    if scheme == 'vmess':
        value = json.loads(decode64(uri[len('vmess://'):].split('#', 1)[0]))
        address = str(value['add']); port = integer(value['port']); user_id = str(uuid.UUID(value['id']))
        q = {'type': value.get('net') or 'tcp', 'path': value.get('path', ''), 'host': value.get('host', ''), 'headerType': value.get('type') or 'none', 'security': 'tls' if value.get('tls') == 'tls' else 'none', 'sni': value.get('sni', ''), 'fp': value.get('fp', ''), 'alpn': value.get('alpn', '')}
        if q['type'] == 'grpc': q['serviceName'] = value.get('path', '')
        users = [{'id': user_id, 'alterId': integer(value.get('aid') or 0, 0, 65535), 'security': value.get('scy') or 'auto'}]
        return {'tag': 'probe', 'protocol': 'vmess', 'settings': {'vnext': [{'address': address, 'port': port, 'users': users}]}, 'streamSettings': transport(q, address)}, value.get('ps') or ''
    if scheme == 'ss':
        rest = uri[len('ss://'):].split('#', 1)[0]
        if '?' in rest:
            rest, query = rest.split('?', 1)
            if urllib.parse.parse_qs(query).get('plugin'): raise Unsupported('plugin')
        if '@' not in rest: rest = decode64(rest)
        auth, server = rest.rsplit('@', 1); auth = urllib.parse.unquote(auth)
        if ':' not in auth: auth = decode64(auth)
        method, password = auth.split(':', 1); endpoint = urllib.parse.urlsplit('ss://' + server)
        if not endpoint.hostname: raise Unsupported('address')
        return {'tag': 'probe', 'protocol': 'shadowsocks', 'settings': {'servers': [{'address': endpoint.hostname, 'port': integer(endpoint.port), 'method': method, 'password': password}]}}, ''
    if scheme not in ('vless', 'trojan'): raise Unsupported('protocol')
    u = urllib.parse.urlsplit(uri)
    if not u.hostname or not u.username: raise Unsupported('address')
    q = {k: v[-1] for k, v in urllib.parse.parse_qs(u.query, keep_blank_values=True).items()}
    user = urllib.parse.unquote(u.username)
    if scheme == 'vless':
        user = {'id': str(uuid.UUID(user)), 'encryption': q.get('encryption') or 'none'}
        if q.get('flow'): user['flow'] = q['flow']
        settings = {'vnext': [{'address': u.hostname, 'port': integer(u.port), 'users': [user]}]}
    else:
        settings = {'servers': [{'address': u.hostname, 'port': integer(u.port), 'password': user}]}
    return {'tag': 'probe', 'protocol': scheme, 'settings': settings, 'streamSettings': transport(q, u.hostname, scheme == 'trojan')}, ''

def parse_subscription(raw, maximum=100):
    if len(raw) > LIMIT: raise ValueError('subscription limit')
    text = raw.decode('utf-8-sig').strip()
    if not re.search(r'^[a-zA-Z][a-zA-Z0-9+.-]*://', text, re.M): text = decode64(text)
    lines = [s.strip() for s in text.splitlines() if s.strip() and not s.lstrip().startswith('#')]
    if not lines or any(not re.match(r'^[a-zA-Z][a-zA-Z0-9+.-]*://', s) for s in lines): raise ValueError('subscription format')
    if len(lines) > maximum: raise OverflowError('node limit')
    nodes = []; seen = set()
    for line in lines:
        key = hashlib.sha256(line.encode()).hexdigest()
        if key in seen: continue
        seen.add(key); name = urllib.parse.unquote(line.partition('#')[2]); outbound = None
        try:
            outbound, fallback_name = uri_outbound(line)
            name = name or fallback_name
        except (ValueError, KeyError, TypeError, OverflowError, UnicodeError):
            pass
        name = re.sub(r'[\x00-\x1f\x7f]', '', str(name).encode('utf-8', 'replace').decode('utf-8')).strip()[:100] or 'Сервер ' + str(len(nodes) + 1)
        nodes.append({'id': key, 'name': name, 'outbound': outbound})
    return nodes

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None

class HttpsRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        if urllib.parse.urlsplit(newurl).scheme != 'https': raise ValueError('insecure redirect')
        return super().redirect_request(req, fp, code, msg, headers, newurl)

def fetch_subscription(url):
    request = urllib.request.Request(url, headers={'User-Agent': 'v2rayN/7.0 MoskomMonitor/1.0', 'Accept': 'text/plain,application/octet-stream', 'Accept-Encoding': 'identity'})
    with urllib.request.build_opener(HttpsRedirect()).open(request, timeout=20) as response:
        raw = gzip.GzipFile(fileobj=response).read(LIMIT + 1) if response.headers.get('Content-Encoding', '').lower() == 'gzip' else response.read(LIMIT + 1)
    if len(raw) > LIMIT: raise ValueError('subscription limit')
    return raw

class Api:
    def __init__(self, endpoint, token):
        self.endpoint = endpoint; self.token = token; self.opener = urllib.request.build_opener(NoRedirect())
    def request(self, body=None):
        headers = {'X-Monitor-Token': self.token, 'Accept': 'application/json'}
        if body is not None: headers['Content-Type'] = 'application/json'
        request = urllib.request.Request(self.endpoint, data=None if body is None else json.dumps(body, ensure_ascii=False).encode(), headers=headers)
        with self.opener.open(request, timeout=40 if body and body.get('task') == 'mail' else 20) as response: raw = response.read(200001)
        if len(raw) > 200000: raise ValueError('response limit')
        return json.loads(raw)

def probe(node, xray, targets=TARGETS):
    result = {'id': node['id'], 'name': node['name'], 'status': 'unknown', 'latency_ms': None, 'checked_at': stamp()}
    if node['outbound'] is None: result['status'] = 'unsupported'; return result
    if STOP.is_set(): return result
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0)); port = sock.getsockname()[1]
    config = {'log': {'loglevel': 'none'}, 'inbounds': [{'tag': 'socks', 'listen': '127.0.0.1', 'port': port, 'protocol': 'socks', 'settings': {'auth': 'noauth', 'udp': False}}], 'outbounds': [node['outbound']], 'routing': {'rules': [{'type': 'field', 'inboundTag': ['socks'], 'outboundTag': 'probe'}]}}
    process = None
    try:
        with tempfile.TemporaryDirectory(prefix='moskom-probe-') as directory:
            path = Path(directory, 'config.json'); path.write_text(json.dumps(config), encoding='utf-8'); path.chmod(0o600)
            process = subprocess.Popen([xray, 'run', '-config', str(path)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
            ready = False
            for _ in range(50):
                if STOP.wait(.1) or process.poll() is not None: break
                try:
                    with socket.create_connection(('127.0.0.1', port), timeout=.1): ready = True; break
                except OSError: pass
            if not ready:
                if process.poll() is not None: result['status'] = 'unsupported'
                return result
            result['status'] = 'down'
            for target in targets:
                if STOP.is_set(): result['status'] = 'unknown'; break
                try:
                    completed = subprocess.run(['curl', '--silent', '--show-error', '--proxy', 'socks5h://127.0.0.1:' + str(port), '--noproxy', '', '--proto', '=https', '--connect-timeout', '5', '--max-time', '8', '--output', os.devnull, '--write-out', '%{response_code} %{time_total} %{size_download}', target], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=10, check=False, text=True)
                    fields = completed.stdout.split()
                    if completed.returncode == 0 and len(fields) == 3 and fields[0] == '204' and float(fields[2]) == 0:
                        result.update(status='up', latency_ms=max(1, min(120000, math.ceil(float(fields[1]) * 1000))))
                        break
                    if process.poll() is not None: result['status'] = 'unknown'; break
                except subprocess.TimeoutExpired: pass
            result['checked_at'] = stamp()
    except OSError:
        result['status'] = 'unknown'
    finally:
        if process is not None and process.poll() is None:
            try:
                os.killpg(process.pid, signal.SIGTERM); process.wait(timeout=2)
            except ProcessLookupError: pass
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL); process.wait()
    return result

def cycle(api, settings, nodes, xray, previous, targets=TARGETS):
    snapshot = {n['id']: previous.get(n['id'], {'id': n['id'], 'name': n['name'], 'status': 'unknown', 'latency_ms': None, 'checked_at': None}) for n in nodes}
    def report():
        api.request({'revision': settings['revision'], 'source_status': settings['source_status'], 'nodes': list(snapshot.values())})
    report(); last_report = time.monotonic()
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as executor:
        futures = [executor.submit(probe, node, xray, targets) for node in nodes]
        for future in concurrent.futures.as_completed(futures):
            value = future.result(); snapshot[value['id']] = value
            if time.monotonic() - last_report >= 15:
                report(); last_report = time.monotonic()
    report(); return snapshot

def dispatch_mail(api):
    try:
        mail = api.request({'task': 'mail'})
        if not mail.get('ready'): LOG.warning('Отправка почты не настроена. Проверьте раздел Почта.')
        elif mail.get('failed'): LOG.warning('Письмо не отправлено. Причина доступна в разделе Почта.')
    except Exception as error:
        LOG.warning('Обработка почты не завершена: %s', type(error).__name__)

def mail_loop(endpoint, token):
    api = Api(endpoint, token)
    while not STOP.is_set():
        started = time.monotonic(); dispatch_mail(api)
        STOP.wait(max(1, 60 - (time.monotonic() - started)))

def run(path, once=False):
    config = json.loads(Path(path).read_text(encoding='utf-8'))
    endpoint = config['endpoint']; token = config['token']; xray = str(Path(config['xray']).resolve())
    parsed = urllib.parse.urlsplit(endpoint)
    if parsed.scheme != 'https' or not parsed.hostname or parsed.username or parsed.password or parsed.fragment: raise ValueError('HTTPS endpoint required')
    if not re.fullmatch(r'[0-9a-f]{64}', token): raise ValueError('token format')
    subprocess.run([xray, 'version'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True, timeout=5)
    targets = config.get('targets', TARGETS)
    if not isinstance(targets, list) or not 1 <= len(targets) <= 3 or any(not isinstance(t, str) or urllib.parse.urlsplit(t).scheme != 'https' or not urllib.parse.urlsplit(t).hostname for t in targets): raise ValueError('HTTPS targets required')
    if not once: threading.Thread(target=mail_loop, args=(endpoint, token), daemon=True).start()
    api = Api(endpoint, token); nodes = []; previous = {}; refreshed = 0; revision = None
    while not STOP.is_set():
        started = time.monotonic()
        try:
            settings = api.request()
            if once: dispatch_mail(api)
            if settings['enabled']:
                if revision != settings['revision']: nodes = []; previous = {}; refreshed = 0; revision = settings['revision']
                source_status = 'ok'
                if time.monotonic() - refreshed >= 300:
                    try:
                        url = settings['subscription_url']; u = urllib.parse.urlsplit(url)
                        if u.scheme != 'https' or u.username or u.password or u.fragment: raise ValueError('source URL')
                        nodes = parse_subscription(fetch_subscription(url)); refreshed = time.monotonic()
                    except OverflowError: nodes = []; previous = {}; source_status = 'limit_exceeded'
                    except (ValueError, UnicodeError): nodes = []; previous = {}; source_status = 'parse_error'
                    except (OSError, urllib.error.URLError): source_status = 'fetch_error'
                settings['source_status'] = source_status
                previous = cycle(api, settings, nodes, xray, previous, targets)
                LOG.info('Проверено узлов: %d; доступны: %d; источник: %s', len(nodes), sum(v['status'] == 'up' for v in previous.values()), source_status)
            else:
                nodes = []; previous = {}; refreshed = 0; LOG.info('Мониторинг отключён в админке.')
        except urllib.error.HTTPError as error:
            LOG.warning('Сайт вернул HTTP %d. Проверьте установку, токен и настройки.', error.code)
        except Exception as error:
            # Exception values can contain private URLs or credentials. Log only a class name.
            LOG.warning('Проверка не завершена: %s', type(error).__name__)
        if once: return
        STOP.wait(max(1, 60 - (time.monotonic() - started)))

if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--config', default='/etc/moskom-monitor/config.json'); parser.add_argument('--once', action='store_true'); args = parser.parse_args()
    logging.basicConfig(level=logging.INFO, format='%(asctime)s %(levelname)s %(message)s')
    signal.signal(signal.SIGTERM, lambda *_: STOP.set()); signal.signal(signal.SIGINT, lambda *_: STOP.set())
    try: run(args.config, args.once)
    except Exception as error:
        LOG.error('Запуск невозможен: %s. Проверьте конфигурацию и путь Xray.', type(error).__name__)
        raise SystemExit(1)
