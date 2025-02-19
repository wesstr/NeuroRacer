# main.py

try:
    import ac
except ImportError:
    print("Using test framework for ac!")
    from .tests.ac import ac
    if (not ac):
        print("Failed to import test ac!")
        exit()

import time
import os
import sys
import json
import random
import platform
import config
import threading
from State import State
import traceback
import subprocess
import queue

if platform.architecture()[0] == "64bit":
    dllDir=os.path.dirname(__file__)+'/DLLsx64'
else:
    dllDir=os.path.dirname(__file__)+'/DLLs'

sys.path.insert(0, dllDir)
os.environ['PATH'] = os.environ['PATH'] + ";."

# try:
import winsound
import socketserver
import socket
# except ImportError:
#     winsound = None
#     socketserver = None
#     socket = None

app_state = State()

server = None

command_queue = queue.Queue()

isReply = False
hasPrinted = False

def on_data(data):
    log_some_shit("Some data: {0}".format(data))

class UdpHandler(socketserver.BaseRequestHandler):
    def handle(self):
        global command
        # log_some_shit("Got a command from the commander!")
        # log_some_shit(self.request[0].decode("utf-8"))

        command_queue.put(json.loads(self.request[0].decode("utf-8")))

# Initialize the app
def acMain(ac_version):
    global server
    app_state.app_window = ac.newApp("Neuro Racer")
    ac.setSize(app_state.app_window, 800, 800)
    ac.addRenderCallback(app_state.app_window, on_render)    
    ac.setBackgroundOpacity(app_state.app_window, 0)
    ac.drawBorder(app_state.app_window, 0)
    ac.setTitle(app_state.app_window, "")
    ac.addLabel(app_state.app_window, "")

    # Load in red dot texture
    app_state.red_texture = ac.newTexture(app_state.red_path)

    server = socketserver.UDPServer(("localhost", 50000), UdpHandler)
    
    serverWorkerThread = threading.Thread(target=server.serve_forever)
    serverWorkerThread.daemon = True
    serverWorkerThread.start()

    # p = subprocess.Popen(["C:\\Users\\wes\\source\\repos\\NeuroRacer\\NeuroRacer\\bin\\Debug\\net8.0-windows\\NeuroRacer.exe"])

    log_some_shit("Started UDP server...")

    return "Neuro Racer"

def acUpdate(deltaT):
    global server, command_queue, hasPrinted

    if (not command_queue.empty()):
        
        if (not hasPrinted):
            log_some_shit("Got a command from the commander!")
            hasPrinted = True
        # formatted_json = json.dumps(command, indent=4, sort_keys=True)
        # log_some_shit(formatted_json)

# Render callback function to handle visual cues
def on_render(delta_t):
    global app_state, server, command_queue
    current_time = time.time()

    if (not command_queue.empty()):

        if (not app_state.current_step):
            app_state.current_step = command_queue.get()
    
    # Check if it's time to show the visual cue
    if (app_state.current_step and app_state.current_step['visual_cue']):
        # Render the circle texture
        ac.glQuadTextured(0, 0, 800, 800, app_state.red_texture)

        if (not app_state.current_step_start_time):
            app_state.current_step_start_time = time.time()

        delta_time = current_time - app_state.current_step_start_time
        log_some_shit(repr(delta_time))

        # Visual cue completed go to next step
        if (delta_time >= config.VISUAL_CUE_DURATION):
            app_state.current_step_start_time = None
            app_state.current_step = None

            
    # Play a sound
    if (app_state.current_step and app_state.current_step['audio_cue']):
        if (winsound):
            if (not app_state.sound_playing):
                winsound.PlaySound("apps/python/NeuroRacer/assets/beep.wav", winsound.SND_FILENAME + winsound.SND_ASYNC)
                app_state.sound_playing = True

            if (not app_state.current_step_start_time):
                app_state.current_step_start_time = time.time()

            delta_time = current_time - app_state.current_step_start_time
            log_some_shit(repr(delta_time))

            # Visual cue completed go to next step
            if (delta_time >= config.AUDIO_CUE_DURATION):
                app_state.current_step_start_time = None
                app_state.current_step = None
                app_state.sound_playing = False

def log_some_shit(shit):
    ac.log(shit)
    ac.console(shit)

# Cleanup function when app closes
def acShutdown():
    global server
    ac.deleteApp(app_state.app_window)
    server.shutdown()




