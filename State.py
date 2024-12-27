
import os

class State():
    def __init__(self):
        # Set up the app variables
        self._app_window = 0

        # Path to sound file
        self._sound_path = os.path.join(os.path.dirname(__file__), "assets/beep.wav")
        # Path to red dot image
        self._red_path = os.path.join(os.path.dirname(__file__), "assets/red.png")
        # path to test schedule.json
        self._json_path = os.path.join(os.path.dirname(__file__), "schedule.json")

        # OpenGL red texture fd
        self._red_texture = -1
        # Test schedule dict
        self._test_schedule = {}
        # Current step
        self._current_step = {}
        # Current step start time
        self._current_step_start_time = 0
        # Current step number
        self._current_step_number = 0
        # Currently playing sound
        self._sound_playing = False

        self._gamepad = None

    @property
    def gamepad(self):
        """The gamepad property."""
        return self._gamepad

    @gamepad.setter
    def gamepad(self, value):
        self._gamepad = value

    @property
    def sound_playing(self):
        """The sound_playing property."""
        return self._sound_playing

    @sound_playing.setter
    def sound_playing(self, value):
        self._sound_playing = value

    @property
    def red_path(self):
        """The red_path property."""
        return self._red_path

    @red_path.setter
    def red_path(self, value):
        self._red_path = value

    @property
    def json_path(self):
        """The json_path property."""
        return self._json_path

    @json_path.setter
    def json_path(self, value):
        self._json_path = value

    @property
    def sound_path(self):
        """The sound_path property."""
        return self._sound_path

    @sound_path.setter
    def sound_path(self, value):
        self._sound_path = value

    @property
    def app_window(self):
        """The app_window property."""
        return self._app_window

    @app_window.setter
    def app_window(self, value):
        self._app_window = value

    @property
    def current_step_start_time(self):
        """The current_step_start_time property."""
        return self._current_step_start_time

    @current_step_start_time.setter
    def current_step_start_time(self, value):
        self._current_step_start_time = value

    @property
    def current_step(self):
        return self._current_step

    @current_step.setter
    def current_step(self, value):
        self._current_step = value

    @property
    def red_texture(self):
        """The red_texture property."""
        return self._red_texture

    @red_texture.setter
    def red_texture(self, value):
        self._red_texture = value

    @property
    def test_schedule(self):
        """The test_schedule property."""
        return self._test_schedule

    @test_schedule.setter
    def test_schedule(self, value):
        self._test_schedule = value

    @property
    def current_step_number(self):
        """The current_step_number property."""
        return self._current_step_number

    @current_step_number.setter
    def current_step_number(self, value):
        self._current_step_number = value






