#!/usr/bin/env python333

import os
import sys


# Get the directory of the current script
current_dir = os.path.dirname(os.path.abspath(__file__))

# Get the parent directory
parent_dir = os.path.dirname(current_dir)

# Add the parent directory to the Python path
sys.path.append(parent_dir)

from NeuroRacer import NeuroRacer

def test_main():
    NeuroRacer.acMain(1)

def test_on_render():
    NeuroRacer.acMain(1)
    i = 0
    while (i != 100):
        NeuroRacer.on_render(1)
        i+=1

