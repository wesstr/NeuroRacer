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

if platform.architecture()[0] == "64bit":
    dllDir=os.path.dirname(__file__)+'\DLLsx64'
else:
    dllDir=os.path.dirname(__file__)+'\DLLs'
sys.path.insert(0, dllDir)
os.environ['PATH'] = os.environ['PATH'] + ";."

try:
    import winsound
except ImportError:
    winsound = None

sys.path.append(os.path.join(os.path.dirname(__file__), "libs"))

# from inputs import get_gamepad

# Set up the app variables
app_window = 0

# Path to sound file
sound_path = os.path.join(os.path.dirname(__file__), "assets/beep.wav")
red_path = os.path.join(os.path.dirname(__file__), "assets/red.png")
json_path = os.path.join(os.path.dirname(__file__), "schedule.json")

red_texture = -1
test_schedule = None
current_step = None
current_step_start_time = None
current_step_number = 0
sound_playing = False

# Initialize the app
def acMain(ac_version):
    global app_window, red_texture, test_schedule, current_step, current_step_number
    ac.console("Woo, look at me, im mr. meeseeks!")
    ac.log("Woo, look at me, im mr. meeseeks!")
    app_window = ac.newApp("Neuro Racer")
    ac.setSize(app_window, 800, 800)
    ac.addRenderCallback(app_window, on_render)    
    ac.setBackgroundOpacity(app_window, 0)
    ac.drawBorder(app_window, 0)
    ac.setTitle(app_window, "")
    ac.addLabel(app_window, "")

    # Load in red dot texture
    red_texture = ac.newTexture(red_path)
    
    # Load test schedule
    ac.console("Json path: " + json_path)
    with open(json_path, "r") as json_file:
        test_schedule = json.load(json_file)

    # Get first step
    current_step = test_schedule['test_schedule'][current_step_number]
    log_some_shit("Current step is: {0}".format(json.dumps(current_step)))

    return "Neuro Racer"

# Render callback function to handle visual cues
def on_render(delta_t):
    global red_texture, current_step_start_time, current_step_number, test_schedule, current_step, sound_playing

    current_time = time.time()

    # Select a random time
    if (not "random_number" in current_step and "wait" in current_step):

        between = current_step["wait"]["between"]
        min = between[0]["min"]
        max = between[1]["max"]
        current_step["random_number"] =  random.randrange(min, max)
        log_some_shit("Waiting for {0} seconds before next step.".format(current_step["random_number"]))
        current_step_start_time = current_time

    delta_time = current_time - current_step_start_time

    # We are on the wait step, check to see if enough time has passed to go to next step
    if ("wait" in current_step):
        if (delta_time >= current_step["random_number"]):
            current_step_number+=1
            current_step = test_schedule['test_schedule'][current_step_number]
            current_step_start_time = current_time
            log_some_shit("Current step is: {0}".format(json.dumps(current_step)))
            return
        else: 
            return


    # Check if it's time to show the visual cue
    if ("visual_cue" in current_step):
        # Render the circle texture
        ac.glQuadTextured(0, 0, 800, 800, red_texture)

        # Visual cue completed go to next step
        if (delta_time >= config.VISUAL_CUE_DURATION):
            current_step_number+=1
            current_step = test_schedule['test_schedule'][current_step_number]
            current_step_start_time = time.time()
            log_some_shit("Current step is: {0}".format(json.dumps(current_step)))


    # Play a sound
    if ("audio_cue" in current_step):
        if (winsound):
            if (not sound_playing):
                winsound.PlaySound("apps/python/NeuroRacer/assets/beep.wav", winsound.SND_FILENAME + winsound.SND_ASYNC)
                sound_playing = True

        # Audio cue completed, go to next step
        if (delta_time >= config.AUDIO_CUE_DURATION):
            current_step_number+=1
            current_step = test_schedule['test_schedule'][current_step_number]
            current_step_start_time = time.time()
            sound_playing = False
            log_some_shit("Current step is: {0}".format(json.dumps(current_step)))

    if (current_step_number >= len(test_schedule['test_schedule'])):
        log_some_shit("Test completed!")



def log_some_shit(shit):
    ac.log(shit)
    ac.console(shit)

# Cleanup function when app closes
def acShutdown():
    ac.deleteApp(app_window)
