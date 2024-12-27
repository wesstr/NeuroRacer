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
from State import State

if platform.architecture()[0] == "64bit":
    dllDir=os.path.dirname(__file__)+'/DLLsx64'
else:
    dllDir=os.path.dirname(__file__)+'/DLLs'
sys.path.insert(0, dllDir)
os.environ['PATH'] = os.environ['PATH'] + ";."

try:
    import winsound
except ImportError:
    winsound = None

from libs.inputs.inputs import UnpluggedError, get_gamepad

app_state = State()

# Initialize the app
def acMain(ac_version):
    global state
    ac.console("Woo, look at me, im mr. meeseeks!")
    ac.log("Woo, look at me, im mr. meeseeks!")
    app_state.app_window = ac.newApp("Neuro Racer")
    ac.setSize(app_state.app_window, 800, 800)
    ac.addRenderCallback(app_state.app_window, on_render)    
    ac.setBackgroundOpacity(app_state.app_window, 0)
    ac.drawBorder(app_state.app_window, 0)
    ac.setTitle(app_state.app_window, "")
    ac.addLabel(app_state.app_window, "")

    # Load in red dot texture
    app_state.red_texture = ac.newTexture(app_state.red_path)
    
    # Load test schedule
    ac.console("Json path: " + app_state.json_path)
    with open(app_state.json_path, "r") as json_file:
        app_state.test_schedule = json.load(json_file)

    # Get first step
    app_state.current_step = app_state.test_schedule['test_schedule'][app_state.current_step_number]
    log_some_shit("Current step is: {0}".format(json.dumps(app_state.current_step)))

    try:
        app_state.gamepad = get_gamepad()
    except UnpluggedError:
        log_some_shit("No controller detected!")

    return "Neuro Racer"

# Render callback function to handle visual cues
def on_render(delta_t):
    global app_state

    current_time = time.time()

    # Select a random time
    if (not "random_number" in app_state.current_step and "wait" in app_state.current_step):

        between = app_state.current_step["wait"]["between"]
        min = between[0]["min"]
        max = between[1]["max"]
        app_state.current_step["random_number"] =  random.randrange(min, max)
        log_some_shit("Waiting for {0} seconds before next step.".format(app_state.current_step["random_number"]))
        app_state.current_step_start_time = current_time

    delta_time = current_time - app_state.current_step_start_time

    # We are on the wait step, check to see if enough time has passed to go to next step
    if ("wait" in app_state.current_step):
        if (delta_time >= app_state.current_step["random_number"]):
            app_state.current_step_number+=1
            app_state.current_step = app_state.test_schedule['test_schedule'][app_state.current_step_number]
            app_state.current_step_start_time = current_time
            log_some_shit("Current step is: {0}".format(json.dumps(app_state.current_step)))
            return
        else: 
            return

    # Check if it's time to show the visual cue
    if ("visual_cue" in app_state.current_step):
        # Render the circle texture
        ac.glQuadTextured(0, 0, 800, 800, app_state.red_texture)

        # Visual cue completed go to next step
        if (delta_time >= config.VISUAL_CUE_DURATION):
            app_state.current_step_number+=1
            app_state.current_step = app_state.test_schedule['test_schedule'][app_state.current_step_number]
            app_state.current_step_start_time = time.time()
            log_some_shit("Current step is: {0}".format(json.dumps(app_state.current_step)))

    # Play a sound
    if ("audio_cue" in app_state.current_step):
        if (winsound):
            if (not app_state.sound_playing):
                winsound.PlaySound("apps/python/NeuroRacer/assets/beep.wav", winsound.SND_FILENAME + winsound.SND_ASYNC)
                app_state.sound_playing = True

        # Audio cue completed, go to next step
        if (delta_time >= config.AUDIO_CUE_DURATION):
            app_state.current_step_number+=1
            app_state.current_step = app_state.test_schedule['test_schedule'][app_state.current_step_number]
            app_state.current_step_start_time = time.time()
            app_state.sound_playing = False
            log_some_shit("Current step is: {0}".format(json.dumps(app_state.current_step)))

    if (app_state.current_step_number >= len(app_state.test_schedule['test_schedule'])):
        log_some_shit("Test completed!")

def log_some_shit(shit):
    ac.log(shit)
    ac.console(shit)

# Cleanup function when app closes
def acShutdown():
    ac.deleteApp(app_state.app_window)

