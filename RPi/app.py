#!/usr/bin/python
# -*- coding: utf-8 -*-
# Above sets the encoding, otherwise you could encounter errors while running on the pi.
# Because of different encoding.

# Copyright (c) Microsoft. All rights reserved.
# Licensed under the MIT license.
# Based on
# https://raw.githubusercontent.com/Azure/iot-central-firmware/master/RaspberryPi/app.py

import iotc
from iotc import IOTConnectType, IOTLogLevel
import RPi.GPIO as GPIO
import glob
from random import randint


# Azure IoT Central variables
scopeId = ""
deviceId = ""
deviceKey = "" # This is the primary key

eventInfoField = "EventInfo"
eventWarnField = "EventWarn"
eventErrorField = "EventError"

eventText = ""

iotc = iotc.Device(scopeId, deviceKey, deviceId, IOTConnectType.IOTC_CONNECT_SYMM_KEY)


# RPi variables
deviceName = "Raspberry Pi"

oneWirePin = 4 # Don't change this pin, as it's the default 1-wire pin.
ledPin = 18 # Pin for the LED.

ledStateName = {True: "ON", False: "OFF"}
ledState = False
ledHasChanged = False


# Variables
gCanSend = False # is True when a connection is established
gCounter = 0

telemetryFormat = '{{\
                    "{field}": "{data}"\
                  }}'


#region CALLBACKS
def onconnect(info):
  global gCanSend
  global iotc

  print("- [onconnect] => status:" + str(info.getStatusCode()))
  if info.getStatusCode() == 0:
     if iotc.isConnected():
       gCanSend = True


def onmessagesent(info):
  print("\t- [onmessagesent] => " + str(info.getPayload()))


def oncommand(info):
  print("- [oncommand] => " + info.getTag() + " => " + str(info.getPayload()))
  if str(info.getTag()).lower().__contains__("led"):
    checkLedState(info.getTag().lower())
  # for testing
  else:
    print("No led command!")


def onsettingsupdated(info):
  print("- [onsettingsupdated] => " + info.getTag() + " => " + info.getPayload())

#endregion


# Function sendEvent, so we don't have to write the entire line ( function: iotc.sendEvent(...) )
def sendEvent(field=eventInfoField, data=""):
  global iotc
  iotc.sendEvent(telemetryFormat.format(field=field, data=data))


def sendDeviceProperty():
  global iotc
  iotc.sendProperty("{\"DeviceID\": \"" + deviceName + "\"}")


#region LED

# Check the state of the LED
def checkLedState(cmd):
  global iotc
  global ledState
  global ledHasChanged

  ledHasChanged = False

  if cmd == "ledon":
    # Check if the LED state is false (off)
    if ledState == False:
      ledState = True
      ledHasChanged = True
    else:
      # Check if the LED state is not already on
      print("LED was already on")
      iotc.sendEvent(telemetryFormat.format(field=eventInfoField, data="LED was already on"))
  elif cmd == "ledoff":
    # Check if the LED state is True (on)
    if ledState == True:
      ledState = False
      ledHasChanged = True
    else:
      # Check if the LED state is not already off
      print("LED was already off")
      iotc.sendEvent(telemetryFormat.format(field=eventInfoField, data="LED was already off"))

  changeLedState(ledState)


# Change the LED state
def changeLedState(state):
  global eventText
  global ledHasChanged

  if ledHasChanged:
    if state == True:
      GPIO.output(ledPin, GPIO.HIGH)
      sendEvent(eventInfoField, "LED is on")
    elif state == False:
      GPIO.output(ledPin, GPIO.LOW)
      sendEvent(eventInfoField, "LED is off")
    else:
      eventText = "Invalid state!"
      sendEvent(eventErrorField, eventText)

    sendLedState()
    eventText = ""


# Send the LED state
def sendLedState():
  global iotc
  iotc.sendTelemetry("{ \
        \"LedState\": \"" + str(ledStateName[ledState]) + "\" \
        }")

#endregion

# Temperature Sensor
#region
# Adapted from (Dutch): http://domoticx.com/raspberry-pi-temperatuur-sensor-ds18b20-uitlezen/
def readTempSensor():
  sensorIds = []

  # !!!!!! TEST !!!!!!!

  # Glob: returns a list of paths matching the pathname pattern
  # One-wire temperature sensor should start with 28-00 (eg. 28-0000054871cb)
  # This code processes only 1 1-wire temp sensor.
  pathOldKernel = "/sys/bus/w1/devices/28-00*/w1_slave" # RPi 1,2,3 with old kernel
  pathNewKernel = "/sys/bus/w1/devices/w1_bus_master1/28-00*/w1_slave" # RPi 2,3 with new kernel
  for sensorId in glob.glob(pathNewKernel):
    sensorIds.append(sensorId.split("/")[5])
    print(sensorIds)

  tfile = open(pathNewKernel.replace("28-00*", sensorIds[0]))
  # Read everything from the file and put it in a variable (text).
  text = tfile.read()
  # Close the file after we read it.
  tfile.close()
  # We split the text on every newline (\n)
  # and we select the 2 rule [1]
  secondline = text.split("\n")[1]
  # Split the rule in words, split on space (" ").
  # We select the 10 word
  temperaturedata = secondline.split(" ")[9]
  # We remove the first 2 characters ("t=").
  # We convert it to a float.
  temperature = float(temperaturedata[2:])
  # We divide the temperature value by 1000 to get the right value and return it.
  print(temperature / 1000)
  return temperature / 1000


#endregion

# Get ARM CPU temperature of RPi
# Adapted from: https://www.cyberciti.biz/faq/linux-find-out-raspberry-pi-gpu-and-arm-cpu-temperature-command/
def getCPUtemperature():
    tFile = open('/sys/class/thermal/thermal_zone0/temp')
    temp = float(tFile.read())/1000.0
    return str(temp)


def main():
  global gCounter
  global ledState
  global iotc

  ## Start Configuration

  ## Azure IoT Central
  #region

  # Set Callback functions
  iotc.on("ConnectionStatus", onconnect)
  iotc.on("MessageSent", onmessagesent)
  iotc.on("Command", oncommand)
  iotc.on("SettingsUpdated", onsettingsupdated)

  # Set logging
  iotc.setLogLevel(IOTLogLevel.IOTC_LOGGING_API_ONLY)

  #endregion

  ## Sensor
  #region

  # Use the BCM pin numbering
  GPIO.setmode(GPIO.BCM)
  # Set ledPin to be an output pin
  GPIO.setup(ledPin, GPIO.OUT)

  #endregion

  ## Start Post-Configuration
  iotc.connect()

  # Send the device property and ledstate once at the start of the program, so telemetry is shown in IoT Central.
  if iotc.isConnected() and gCanSend == True:
    sendDeviceProperty()
    sendLedState()


  while iotc.isConnected():
    iotc.doNext() # do the async work needed to be done for MQTT
    if gCanSend == True:

      # Do this when gCounter is 20.
      if gCounter % 20 == 0:
        gCounter = 0
        print("Sending telemetry..")
        # The key in the json that will be sent to IoT Central must be equal to the Name in IoT Central!!
        # eg. "Temperature" in the json must equal (case sensitive) the Name field "Temperature" in IoT Central
        # if you want real temp data use function: readTempSensor()
        iotc.sendTelemetry("{ \
                \"Temperature\": " + str(randint(0, 25)) + ", \
                \"Pressure\": " + str(randint(850, 1150)) + ", \
                \"Humidity\": " + str(randint(0, 100)) + ", \
                \"CPUTemperature\": " + str(getCPUtemperature()) + ", \
        }")

      gCounter += 1

if __name__ == '__main__':
    main()