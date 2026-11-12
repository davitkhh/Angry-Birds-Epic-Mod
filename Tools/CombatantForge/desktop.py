#!/usr/bin/env python3
"""Combatant Forge as a desktop window (GTK + WebKit). Falls back to the browser when those aren't installed.

    combatant-forge                 opens the last game project, or asks for one
    combatant-forge --project DIR   uses DIR and remembers it
"""
import argparse
import json
import math
import os
import sys
import threading
import webbrowser

APP_DIR = os.path.dirname(os.path.abspath(__file__))
CONFIG = os.path.join(os.environ.get('XDG_CONFIG_HOME') or os.path.expanduser('~/.config'), 'CombatantForge', 'config.json')
ZOOM_MIN, ZOOM_MAX = 0.5, 3.0
MARKER = os.path.join('Assets', 'BalancingData', 'PigBalancingData.json')


def is_game(path):
    return bool(path) and os.path.exists(os.path.join(path, MARKER))


def load_gtk():
    try:
        import gi
        gi.require_version('Gtk', '3.0')
        gi.require_version('WebKit2', '4.1')
        from gi.repository import Gtk, WebKit2
        return Gtk, WebKit2
    except (ImportError, ValueError):
        return None, None


def load_config():
    try:
        config = json.load(open(CONFIG, encoding='utf-8'))
        return config if isinstance(config, dict) else {}
    except (OSError, ValueError):
        return {}


def save_config(**values):
    config = load_config()
    config.update(values)
    os.makedirs(os.path.dirname(CONFIG), exist_ok=True)
    with open(CONFIG, 'w', encoding='utf-8') as f:
        json.dump(config, f)


def remembered_project():
    return load_config().get('project')


def remember_project(path):
    save_config(project=path)


def ask_for_project(Gtk):
    while True:
        dialog = Gtk.FileChooserDialog(title='Choose the game project folder (the one with Assets in it)', action=Gtk.FileChooserAction.SELECT_FOLDER)
        dialog.add_buttons('Cancel', Gtk.ResponseType.CANCEL, 'Open', Gtk.ResponseType.OK)
        answer = dialog.run()
        path = dialog.get_filename()
        dialog.destroy()
        while Gtk.events_pending():
            Gtk.main_iteration()
        if answer != Gtk.ResponseType.OK:
            return None
        if is_game(path):
            return path
        notice = Gtk.MessageDialog(message_type=Gtk.MessageType.ERROR, buttons=Gtk.ButtonsType.OK, text='That folder has no %s.' % MARKER)
        notice.run()
        notice.destroy()


def find_project(cli_path, Gtk):
    inside = os.path.abspath(os.path.join(APP_DIR, '..', '..'))
    for path in (cli_path, os.environ.get('FORGE_ROOT'), inside if is_game(inside) else None, remembered_project()):
        if is_game(path):
            return os.path.abspath(path)
    if cli_path:
        sys.exit('%s is not a game project: it has no %s' % (cli_path, MARKER))
    if Gtk:
        return ask_for_project(Gtk)
    sys.exit('Run it once with --project /path/to/the/game')


def build_window(Gtk, WebKit2, url, jobs_running, choose_save_path=None):
    """Returns (window, webview). choose_save_path(suggested name) -> path or None; defaults to a save dialog."""
    window = Gtk.Window(title='Combatant Forge')
    window.set_default_size(1500, 900)
    view = WebKit2.WebView()
    window.add(view)

    # Ctrl+scroll and Ctrl +/- zoom the whole window, like a browser (Ctrl+0 resets); the zoom is remembered between runs.
    from gi.repository import Gdk

    def set_zoom(level):
        view.set_zoom_level(min(ZOOM_MAX, max(ZOOM_MIN, level)))
    try:
        set_zoom(float(load_config().get('zoom', 1.0)))
    except (TypeError, ValueError):
        pass

    def on_scroll(_view, event):
        if not event.get_state() & Gdk.ModifierType.CONTROL_MASK:
            return False
        if event.direction == Gdk.ScrollDirection.SMOOTH:
            dy = event.get_scroll_deltas()[2]
        else:
            dy = {Gdk.ScrollDirection.UP: -1, Gdk.ScrollDirection.DOWN: 1}.get(event.direction, 0)
        if dy:
            set_zoom(view.get_zoom_level() * math.exp(-dy * 0.1))
        return True
    view.connect('scroll-event', on_scroll)

    zoom_keys = {'plus': 1.1, 'equal': 1.1, 'KP_Add': 1.1, 'minus': 1 / 1.1, 'underscore': 1 / 1.1, 'KP_Subtract': 1 / 1.1}

    def on_key(_view, event):
        if not event.get_state() & Gdk.ModifierType.CONTROL_MASK:
            return False
        name = Gdk.keyval_name(event.keyval)
        if name in zoom_keys:
            set_zoom(view.get_zoom_level() * zoom_keys[name])
            return True
        if name in ('0', 'KP_0'):
            set_zoom(1.0)
            return True
        return False
    view.connect('key-press-event', on_key)

    def save_dialog(name):
        dialog = Gtk.FileChooserDialog(title='Save', parent=window, action=Gtk.FileChooserAction.SAVE)
        dialog.add_buttons('Cancel', Gtk.ResponseType.CANCEL, 'Save', Gtk.ResponseType.OK)
        dialog.set_do_overwrite_confirmation(True)
        dialog.set_current_name(name)
        path = dialog.get_filename() if dialog.run() == Gtk.ResponseType.OK else None
        dialog.destroy()
        return path
    chooser = choose_save_path or save_dialog

    def on_download(_session, download):
        def decide(dl, suggested):
            path = chooser(suggested)
            if path:
                dl.set_allow_overwrite(True)
                dl.set_destination(path)
            else:
                dl.cancel()
            return True
        download.connect('decide-destination', decide)
    view.get_context().connect('download-started', on_download)

    def on_close(*_):
        if jobs_running():
            notice = Gtk.MessageDialog(parent=window, message_type=Gtk.MessageType.QUESTION, buttons=Gtk.ButtonsType.YES_NO,
                                       text='An upload or Claude job is still running. Quit and stop it?')
            answer = notice.run()
            notice.destroy()
            if answer != Gtk.ResponseType.YES:
                return True
        save_config(zoom=round(view.get_zoom_level(), 3))
        Gtk.main_quit()
        return False
    window.connect('delete-event', on_close)
    view.load_uri(url)
    return window, view


def main():
    parser = argparse.ArgumentParser(description='Combatant Forge')
    parser.add_argument('--project', help='the game project folder')
    args = parser.parse_args()
    Gtk, WebKit2 = load_gtk()
    project = find_project(args.project, Gtk)
    if not project:
        return
    remember_project(project)
    os.environ['FORGE_ROOT'] = project
    sys.path.insert(0, APP_DIR)
    import forge
    server = forge.make_server(0)
    url = 'http://127.0.0.1:%d/' % server.server_address[1]
    threading.Thread(target=server.serve_forever, daemon=True).start()
    if Gtk:
        window, _ = build_window(Gtk, WebKit2, url, forge.jobs_running)
        window.show_all()
        Gtk.main()
    else:
        print('GTK/WebKit not found, opening your browser instead:', url)
        print('For the desktop window: sudo apt install python3-gi gir1.2-webkit2-4.1')
        webbrowser.open(url)
        try:
            threading.Event().wait()
        except KeyboardInterrupt:
            pass
    os._exit(0)


if __name__ == '__main__':
    main()
