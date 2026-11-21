#!/usr/bin/env python3
"""Combatant Forge: python3 Tools/CombatantForge/forge.py [--port 8765] [--no-browser]"""
import argparse
import base64
import json
import mimetypes
import os
import re
import threading
import time
import traceback
import urllib.parse
import uuid
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import gamedata as g
import effects
import existing
import projectiles
import portable
import publish as p

WEB = os.path.join(g.HERE, 'web')
PROJECTS = os.path.join(g.DATA, 'projects')
existing.DIR = os.path.join(PROJECTS, 'existing')
projectiles.DIR = os.path.join(PROJECTS, 'projectiles')
jobs = {}
controls = {}
publish_lock = threading.Lock()


def project_path(pid):
    if not re.fullmatch(r'[a-z0-9_]+', pid or ''):
        raise ValueError('bad combatant id')
    return os.path.join(PROJECTS, pid + '.json')


def save_project(project):
    if project.get('kind') == 'existing':
        return existing.save(project)
    if project.get('kind') == 'projectile':
        return projectiles.save(project)
    os.makedirs(PROJECTS, exist_ok=True)
    tmp = project_path(project['id']) + '.tmp'
    with open(tmp, 'w', encoding='utf-8') as f:
        json.dump(project, f, indent=1, ensure_ascii=False)
    os.replace(tmp, project_path(project['id']))


def load_project(pid, kind=None):
    path = existing.project_path(pid) if kind == 'existing' else project_path(pid)
    return p.migrate_project(json.load(open(path, encoding='utf-8')))


def list_projects():
    if not os.path.isdir(PROJECTS):
        return []
    out = []
    for name in sorted(os.listdir(PROJECTS)):
        if name.endswith('.json'):
            d = json.load(open(os.path.join(PROJECTS, name), encoding='utf-8'))
            out.append({'id': d['id'], 'name': d.get('name', ''), 'rig': d.get('rig', ''), 'publishedAt': d.get('publishedAt')})
    return out


def trash_project(pid):
    """Moves the project file to projects/.trash (named with the time), so a delete can be undone by hand."""
    path = project_path(pid)
    if not os.path.exists(path):
        return None
    folder = os.path.join(PROJECTS, '.trash')
    os.makedirs(folder, exist_ok=True)
    target = os.path.join(folder, '%s-%s.json' % (pid, time.strftime('%Y%m%d-%H%M%S')))
    os.replace(path, target)
    return target


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
                # Merge into what's on disk: the project may have been edited while this ran. A delete returns nothing.
                if result is not None:
                    try:
                        current = load_project(result['id'], result.get('kind'))
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
            if what == 'bells':
                import bells
                return self.send(bells.preview_config())
            if what == 'skills':
                return self.send(g.skills_catalog())
            if what == 'existing':
                return self.send(existing.catalog())
            if what == 'variant_presets':
                return self.send(existing.variant_presets())
            if what == 'projectile_new':
                return self.send(projectiles.blank())
            if what == 'projectiles_list':
                return self.send(projectiles.listing())
            if what == 'projectile_project':
                return self.send(projectiles.load(parts[2]))
            if what == 'projectile_uploaded':
                return self.send(projectiles.from_uploaded(parts[2]))
            if what == 'projectile_from_game':
                return self.send(projectiles.from_game(parts[2]))
            if what == 'projectile_layers':
                return self.send(projectiles.game_layers(parts[2]))
            if what == 'projectile_export':
                data = projectiles.export_bytes(projectiles.load(parts[2]))
                self.send_response(200)
                self.send_header('Content-Type', 'application/json')
                self.send_header('Content-Disposition', 'attachment; filename="%s.forgeprojectile"' % parts[2])
                self.send_header('Content-Length', str(len(data)))
                self.send_header('Cache-Control', 'no-store')
                self.end_headers()
                self.wfile.write(data)
                return
            if what == 'fx_library':
                return self.send(effects.library_items())
            if what == 'fx':
                if parts[2].startswith(projectiles.PREFIX):
                    return self.send(None)
                return self.send(effects.info(parts[2]))
            if what == 'allfx':
                return self.send(p.all_skill_effects())
            if what == 'abilityfx':
                # a skill's effects as the game plays them: its own Forge list, else its AssetId's
                skill_id, asset = parts[2], (parts[3] if len(parts) > 3 else '')
                return self.send(p.skill_fx_of(skill_id, asset))
            if what == 'fx_names':
                return self.send(effects.effect_names())
            if what == 'skillfx':
                return self.send(effects.skill_effects(parts[2]))
            if what == 'projectile':
                if parts[2].startswith(projectiles.PREFIX):
                    return self.send(projectiles.published_info(parts[2]))
                return self.send(g.projectile_info(parts[2]))
            if what == 'projectiles':
                # the game's projectiles and the ones uploaded from the Projectiles section
                return self.send(projectiles.published_names() + g.projectile_catalog())
            if what == 'powerlevels':
                return self.send(g.power_levels())
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
        if head == 'fximage':
            return self.send(effects.image_png(parts[1]), 'image/png', cache=True)
        if head in ('projectileart', 'projectileicon') and parts[1].startswith(projectiles.PREFIX):
            return self.send(projectiles.published_art(parts[1])[0], 'image/png')
        if head == 'projectileart':
            return self.send(g.projectile_art(parts[1]), 'image/png', cache=True)
        if head == 'projectileicon':
            return self.send(g.projectile_icon(parts[1]), 'image/png', cache=True)
        if head == 'pigicon':
            return self.send(existing.icon(parts[1]), 'image/png')
        if head == 'rigicon':
            return self.send(g.rig_icon(parts[1], parts[2] if len(parts) > 2 else ''), 'image/png', cache=True)
        if head == 'sprite':
            return self.send(g.sprite_png(parts[1]), 'image/png', cache=not parts[1].startswith('upload:'))
        if head == 'ngui':
            return self.send(g.ngui_png(parts[1], parts[2]), 'image/png', cache=True)
        if head == 'bellsound':
            import bells
            return self.send(bells.sound_wav(), 'audio/wav', cache=True)
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
                if body.get('kind') == 'existing':
                    return self.send({'ok': True})
                if body.get('kind') == 'projectile':
                    if renamed_from and renamed_from != body['id'] and os.path.exists(projectiles.path(renamed_from)):
                        os.remove(projectiles.path(renamed_from))
                    return self.send({'ok': True})
                if renamed_from and renamed_from != body['id'] and not body.get('publishedAt') and os.path.exists(project_path(renamed_from)):
                    os.remove(project_path(renamed_from))
                return self.send({'ok': True})
            if what == 'publish':
                if body.get('kind') == 'existing':
                    return self.send({'job': start_job(lambda log, register: existing.update(body, log))})
                return self.send({'job': start_job(lambda log, register: p.publish(body, log))})
            if what == 'delete':
                pid = body['id']
                project = load_project(pid)
                if body.get('fromGame') and project.get('publishedAt'):
                    def work(log, register):
                        p.unpublish(project, log)
                        log('Moved the project to %s' % os.path.relpath(trash_project(pid), g.DATA))
                        return None
                    return self.send({'job': start_job(work)})
                return self.send({'trashed': trash_project(pid)})
            if what == 'projectile_duplicate':
                copy_ = projectiles.duplicate(body)
                projectiles.save(copy_)
                return self.send(copy_)
            if what == 'projectile_publish':
                project = projectiles.load(body['id'])
                return self.send({'name': projectiles.publish(project), 'project': project})
            if what == 'projectile_delete':
                return self.send({'trashed': projectiles.trash(body['id'])})
            if what == 'projectile_import':
                project = projectiles.import_bytes(base64.b64decode(body['data'].split(',', 1)[-1]))
                projectiles.save(project)
                return self.send({'project': project, 'library': g.library()})
            if what == 'existing_open':
                project, note = existing.open_project(body['id'])
                return self.send({'project': project, 'note': note})
            if what == 'existing_duplicate':
                project = existing.duplicate(body['id'], {x['id'] for x in list_projects()})
                save_project(project)
                return self.send({'project': p.migrate_project(project)})
            if what == 'existing_variant':
                parent, suffix, ratios = body['parent'], body['suffix'], body.get('ratios') or {}

                def work(log, register):
                    job_result['nameId'] = existing.add_variant(parent, suffix, log, ratios, body.get('template'))
                    return None
                job_result = {}
                job_id = start_job(work)
                jobs[job_id]['variant'] = job_result
                return self.send({'job': job_id})
            if what == 'existing_delete':
                name_id = body['id']
                return self.send({'job': start_job(lambda log, register: existing.delete_variant(name_id, log))})
            if what == 'existing_tick':
                return self.send({'unticked': existing.set_tick(body['parent'], body['variant'], bool(body['on']))})
            if what == 'validate':
                return self.send({'problems': p.validate(body)})
            if what == 'ability':
                uid = body['uid']
                project = body['project']

                def work(log, register):
                    ability = next(a for a in project['abilities'] if a['uid'] == uid)
                    return p.run_claude_ability(project, ability, log, register)
                return self.send({'job': start_job(work, stoppable=True)})
            if what == 'recipe':
                uid, ruid = body['uid'], body['recipe']
                project = body['project']

                def work(log, register):
                    ability = next(a for a in project['abilities'] if a['uid'] == uid)
                    recipe = next(r for r in ability.get('recipes') or [] if r['uid'] == ruid)
                    return p.run_claude_recipe(project, ability, recipe, log, register)
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
    # The library's Effects category reads every effect prefab once (~20 s), then a cache: start it now.
    threading.Thread(target=lambda: effects.library_items(), daemon=True).start()
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
