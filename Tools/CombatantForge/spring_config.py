"""Per-pig spring constants for spring drones (SpringDrone.cs).

The game reads Assets/Resources/SpringDrones.json: {"springs": [{"pig": <NameId>, "constant": N/m, "variance": N/m}]}.
A pig without an entry uses its finish's default (SpringDrone.LavenderSpringConstant / SteelSpringConstant), and each
drone rolls its own constant within +- variance when it spawns.

A project keeps its spring as project['spring'] = {'constant': .., 'variance': ..}; no key means the defaults.
"""
import json
import os

import gamedata as g

PATH = os.path.join(g.ASSETS, 'Resources', 'SpringDrones.json')


def load():
    """{pig NameId: {'constant': .., 'variance': ..}}"""
    try:
        rows = json.load(open(PATH, encoding='utf-8')).get('springs') or []
    except (OSError, ValueError):
        return {}
    return {r['pig']: {'constant': float(r.get('constant', 0)), 'variance': float(r.get('variance', 0))} for r in rows if r.get('pig')}


def get(pig_id):
    return load().get(pig_id)


def _save(table):
    rows = [{'pig': pig, 'constant': round(v['constant'], 3), 'variance': round(v['variance'], 3)} for pig, v in sorted(table.items())]
    text = json.dumps({'springs': rows}, indent=2) + '\n'
    os.makedirs(os.path.dirname(PATH), exist_ok=True)
    with open(PATH, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)


def clean(spring):
    """A project's spring as stored, or None for the defaults."""
    if not spring:
        return None
    try:
        constant = float(spring.get('constant') or 0)
        variance = abs(float(spring.get('variance') or 0))
    except (TypeError, ValueError):
        return None
    if constant <= 0:
        return None
    return {'constant': constant, 'variance': variance}


def publish(pig_id, project, log=None):
    """Writes (or removes) pig_id's entry from the project. Returns True when the file changed."""
    looks = g.LOOK_RIGS.get(project.get('rig'), ())
    spring = clean(project.get('spring')) if project.get('variant') in looks else None
    table = load()
    before = table.get(pig_id)
    if spring:
        table[pig_id] = spring
    else:
        table.pop(pig_id, None)
    if table.get(pig_id) == before:
        return False
    _save(table)
    if log:
        if spring:
            log('Spring: %g N/m +- %g for %s' % (spring['constant'], spring['variance'], pig_id))
        else:
            log('Spring: default for %s' % pig_id)
    return True
