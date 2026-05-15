import ctypes
import os
import time

# Path to the shared library
lib_path = "....EmotiBitLib.so"

if not os.path.exists(lib_path):
    print(f"Error: Library not found at {lib_path}")
    exit(1)

# Load the library
lib = ctypes.CDLL(lib_path)

# Define function signatures
lib.InitEmotiBit.restype = None
lib.UpdateEmotiBit.restype = None
lib.IsConnected.restype = ctypes.c_bool
lib.GetBatteryLevel.restype = ctypes.c_void_p  # Returns a pointer to a string

def get_battery_level():
    ptr = lib.GetBatteryLevel()
    if ptr:
        # Assuming the shared library returns a CoTaskMem string (ANSI)
        # Note: In a real scenario, we might need a way to free this memory if it's allocated by the library.
        # But for this test, we just want to see if we can call it.
        result = ctypes.string_at(ptr).decode('utf-8')
        return result
    return "?"

print("Initializing EmotiBit...")
lib.InitEmotiBit()

print("Updating EmotiBit state (searching for devices)...")
for _ in range(5):
    lib.UpdateEmotiBit()
    time.sleep(1)
    connected = lib.IsConnected()
    battery = get_battery_level()
    print(f"Connected: {connected}, Battery: {battery}")

print("Test complete.")
