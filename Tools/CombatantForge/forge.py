#!/usr/bin/env python3
"""Combatant Forge: python3 Tools/CombatantForge/forge.py [--port 8765] [--no-browser]"""
import argparse
import base64
import json
import mimetypes
import os
import re
import threading
import traceback
import urllib.parse
import uuid
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import gamedata as g
import portable
import publish as p

WEB = os.path.join(g.HERE, 'web')
PROJECTS = os.path.join(g.DATA, 'projects')
jobs = {}
controls = {}
publish_lock = threading.Lock()


def project_path(pid):
    if not re.fullmatch(r'[a-z0-9_]+', pid or ''):
        raise ValueError('bad combatant id')
    return os.path.join(PROJECTS, pid + '.json')


def save_project(project):
    os.makedirs(PROJECTS, exist_ok=True)
    tmp = project_path(project['id']) + '.tmp'
    with open(tmp, 'w', encoding='utf-8') as f:
        json.dump(project, f, indent=1, ensure_ascii=False)
    os.replace(tmp, project_path(project['id']))


def load_project(pid):
    return p.migrate_project(json.load(open(project_path(pid), encoding='utf-8')))


def list_projects():
    if not os.path.isdir(PROJECTS):
        return []
    out = []
    for name in sorted(os.listdir(PROJECTS)):
        if name.endswith('.json'):
            d = json.load(open(os.path.join(PROJECTS, name), encoding='utf-8'))
            out.append({'id': d['id'], 'name': d.get('name', ''), 'rig': d.get('rig', ''), 'publishedAt': d.get('publishedAt')})
    return out


def start_job(work, stoppable=False):
    job_id = uuid.uuid4().hex
    job = jobs[job_id] = {'log': [], 'done': False, 'error': None, 'project': None, 'stoppable': stoppable, 'stopped': False}
    controls[job_id] = {'proc': None}

    def runner():
        try:
            if publish_lock.locked():
                job['log'].append('Waiting for the other job to finish...')
            with publish_lock:
                if job['stopped']:
                    raise ValueError('Stopped before it started')
                result = work(job['log'].append, lambda proc: controls[job_id].update(proc=proc))
                job['project'] = result
                # Merge into what's on disk: the project may have been edited while this ran.
                try:
                    current = load_project(result['id'])
                    save_project(p.merge_job_result(current, result, texts=stoppable))
                except FileNotFoundError:
                    save_project(result)
        except Exception as e:
            job['error'] = str(e)
            job['log'].append('Stopped.' if job['stopped'] else 'FAILED: %s' % e)
            if not job['stopped']:
                traceback.print_exc()
        job['done'] = True
    threading.Thread(target=runner, daemon=True).start()
    return job_id


def stop_job(job_id):
    job = jobs[job_id]
    if job['done'] or not job['stoppable']:
        return
    job['stopped'] = True
    job['log'].append('Stopping. Files Claude already changed stay changed.')
    proc = controls[job_id]['proc']
    if proc:
        proc.terminate()


class Handler(BaseHTTPRequestHandler):
    protocol_version = 'HTTP/1.1'

    def log_message(self, *args):
        pass

    def send(self, data, ctype='application/json', code=200, cache=False):
        if not isinstance(data, bytes):
            data = json.dumps(data, ensure_ascii=False).encode()
        self.send_response(code)
        self.send_header('Content-Type', ctype)
        self.send_header('Content-Length', str(len(data)))
        self.send_header('Cache-Control', 'max-age=86400' if cache else 'no-store')
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        url = urllib.parse.urlparse(self.path)
        parts = [urllib.parse.unquote(x) for x in url.path.split('/') if x]
        try:
            self.route_get(parts, urllib.parse.parse_qs(url.query))
        except BrokenPipeError:
            pass
        except Exception as e:
            traceback.print_exc()
            self.send({'error': str(e)}, code=500)

    def route_get(self, parts, query):
        if not parts:
            return self.static('index.html')
        head = parts[0]
        if head == 'api':
            what = parts[1]
            if what == 'bootstrap':
                return self.send({'rigs': g.rig_catalog(), 'colors': g.SKILL_COLORS, 'projects': list_projects(), 'unity': p.unity_running(), 'claude': bool(p.claude_binary())})
            if what == 'rig':
                return self.send(g.rig_json(parts[2]))
            if what == 'library':
                return self.send(g.library())
            if what == 'skills':
                return self.send(g.skills_catalog())
            if what == 'icons':
                return self.send(g.icon_catalog())
            if what == 'stats':
                return self.send(g.template_stats(parts[2]))
            if what == 'project':
                return self.send(load_project(parts[2]))
            if what == 'export':
                data = portable.export_bytes(load_project(parts[2]))
                self.send_response(200)
                self.send_header('Content-Type', 'application/zip')
                self.send_header('Content-Disposition', 'attachment; filename="%s.forge"' % parts[2])
                self.send_header('Content-Length', str(len(data)))
                self.send_header('Cache-Control', 'no-store')
                self.end_headers()
                self.wfile.write(data)
                return
            if what == 'job':
                job = jobs[parts[2]]
                return self.send(job)
        if head == 'rigicon':
            return self.send(g.rig_icon(parts[1], parts[2] if len(parts) > 2 else ''), 'image/png', cache=True)
        if head == 'sprite':
            return self.send(g.sprite_png(parts[1]), 'image/png', cache=not parts[1].startswith('upload:'))
        if head == 'ngui':
            return self.send(g.ngui_png(parts[1], parts[2]), 'image/png', cache=True)
        if head == 'font':
            return self.send(open(os.path.join(g.ASSETS, 'Font', 'AngryBirds-Regular.otf'), 'rb').read(), 'font/otf', cache=True)
        return self.static('/'.join(parts))

    def static(self, rel):
        path = os.path.normpath(os.path.join(WEB, rel))
        if not path.startswith(WEB) or not os.path.isfile(path):
            return self.send({'error': 'not found'}, code=404)
        self.send(open(path, 'rb').read(), mimetypes.guess_type(path)[0] or 'application/octet-stream')

    def do_POST(self):
        parts = [x for x in self.path.split('/') if x]
        body = json.loads(self.rfile.read(int(self.headers.get('Content-Length', 0))) or b'{}')
        try:
            what = parts[1]
            if what == 'upload':
                saved = [g.add_upload(body['side'], body['cat'], f['name'], f['data']) for f in body['files']]
                return self.send({'saved': saved, 'library': g.library()})
            if what == 'save':
                renamed_from = body.pop('renamedFrom', None)
                save_project(body)
                if renamed_from and renamed_from != body['id'] and not body.get('publishedAt') and os.path.exists(project_path(renamed_from)):
                    os.remove(project_path(renamed_from))
                return self.send({'ok': True})
            if what == 'publish':
                return self.send({'job': start_job(lambda log, register: p.publish(body, log))})
            if what == 'validate':
                return self.send({'problems': p.validate(body)})
            if what == 'ability':
                uid = body['uid']
                project = body['project']

                def work(log, register):
                    ability = next(a for a in project['abilities'] if a['uid'] == uid)
                    return p.run_claude_ability(project, ability, log, register)
                return self.send({'job': start_job(work, stoppable=True)})
            if what == 'job_stop':
                stop_job(body['job'])
                return self.send({'ok': True})
            if what == 'import':
                taken = {x['id'] for x in list_projects()}
                project, notes = portable.import_bytes(base64.b64decode(body['data'].split(',', 1)[-1]), taken)
                save_project(project)
                return self.send({'project': project, 'notes': notes, 'library': g.library()})
            self.send({'error': 'not found'}, code=404)
        except Exception as e:
            traceback.print_exc()
            self.send({'error': str(e)}, code=400)


def make_server(port):
    # Local tool: it writes into the game's files, so it only listens on this machine.
    return ThreadingHTTPServer(('127.0.0.1', port), Handler)


def jobs_running():
    return [j for j in jobs.values() if not j['done']]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--port', type=int, default=8765)
    parser.add_argument('--no-browser', action='store_true')
    args = parser.parse_args()
    server = make_server(args.port)
    url = 'http://127.0.0.1:%d/' % args.port
    print('Combatant Forge running at', url)
    if not args.no_browser:
        threading.Timer(0.3, webbrowser.open, (url,)).start()
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == '__main__':
    main()
