# Copyright (c) Microsoft. All rights reserved.
# Licensed under the MIT license.
# Based on
# https://raw.githubusercontent.com/Azure/iot-central-firmware/master/RaspberryPi/app.py

import iotc
from iotc import IOTConnectType, IOTLogLevel
import RPi.GPIO as GPIO
import time
import os
import sys
import glob
from random import randint


# IoT Central variables
scopeId = ""
deviceId = ""
deviceKey = "" # This is the primary key

eventInfoField = "EventInfo"
eventWarnField = "EventWarn"
eventErrorField = "EventError"

eventText = ""

iotc = iotc.Device(scopeId, deviceKey, deviceId, IOTConnectType.IOTC_CONNECT_SYMM_KEY)
iotc.setLogLevel(IOTLogLevel.IOTC_LOGGING_API_ONLY)


# RPi variables
deviceName = "Raspberry Pi"

oneWirePin = None
ledPin = 18

ledState = False


# Variables
gCanSend = False
gCounter = 0

telemetryFormat = '{{\
                    "{field}": "{data}"\
                  }}'




#region CALLBACKS
def onconnect(info):
  global gCanSend
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

iotc.on("ConnectionStatus", onconnect)
iotc.on("MessageSent", onmessagesent)
iotc.on("Command", oncommand)
iotc.on("SettingsUpdated", onsettingsupdated)

#endregion


# Function sendEvent, so we don't have to write the entire line ( function: iotc.sendEvent(...) )
def sendEvent(field=eventInfoField, data=""):
  iotc.sendEvent(telemetryFormat.format(field=field, data=data))


def sendDeviceProperty():
  iotc.sendProperty("{\"DeviceID\": \"" + deviceName + "\"}")


#region LED

# Check the state of the LED
def checkLedState(cmd):
  global ledState
  if cmd == "ledon":
    # Check if the LED state is false (off)
    if ledState == False:
      ledState = True
    else:
      # Check if the LED state is not already on
      print("LED was already on")
      iotc.sendEvent(telemetryFormat.format(field=eventInfoField, data="LED was already on"))
  elif cmd == "ledoff":
    # Check if the LED state is True (on)
    if ledState == True:
      ledState = False
    else:
      # Check if the LED state is not already off
      print("LED was already off")
      iotc.sendEvent(telemetryFormat.format(field=eventInfoField, data="LED was already off"))

  changeLedState()


# Change the LED state
def changeLedState(state=ledState):
  if state == True:
    #GPIO.output(ledPin, GPIO.HIGH)
    sendEvent(eventInfoField, "LED is on")
  elif state == False:
    #GPIO.output(ledPin, GPIO.LOW)
    sendEvent(eventInfoField, "LED is off")
  else:
    eventText = "Invalid state! LED didn't change. "
    sendEvent(eventErrorField, eventText)

  sendLedState()
  eventText = ""


# Send the LED state
def sendLedState():
  iotc.sendTelemetry("{ \
        \"LedState\": " + str(ledState) + ", \
        }")

#endregion

#### TEST !!!!! ####
#region Temperature Sensor
# Source (Dutch): http://domoticx.com/raspberry-pi-temperatuur-sensor-ds18b20-uitlezen/
def readTempSensor():
  # Define an array (temp).
  temp = {}
  sensorIds = []

  # !!!!!! TEST !!!!!!!

  # Go to "/sys/bus/w1/devices/" on your pi, what comes after that is your sensorId.
  # It should start with 28-00
  for sensorId in glob.glob("/sys/bus/w1/devices/28-00*/w1_slave"):
    sensorIds.append(sensorId.split("/")[5])
    print(sensorIds)

  #sensorIds = ["28-0000054871cb"]

  # loop till all sensorIds have been read.
  for sensor in range(len(sensorIds)):
    #tfile = open("/sys/bus/w1/devices/"+ sensorids[sensor] +"/w1_slave") #RPi 1,2 with old kernel.
    tfile = open("/sys/bus/w1/devices/"+ sensorIds[sensor] +"/w1_slave") #RPi 2,3 with new kernel.
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
    # We divide the temperature value by 1000 to get the right value.
    temp[sensor] = temperature / 1000
    # Print the data to the console.
    print("sensor", sensor, "=", temp[sensor], "°C.")


#endregion

# Get CPU temperature of RPi
# Source: https://gist.github.com/elbruno/6895e95c97e8dd3318a8d7878231a41f
def getCPUtemperature():
    res = os.popen('vcgencmd measure_temp').readline()
    return(res.replace("temp=","").replace("'C\n",""))


def main():
  iotc.connect()
  GPIO.setmode(GPIO.BCM)
  GPIO.setup(ledPin, GPIO.OUT)

  while iotc.isConnected():
    iotc.doNext() # do the async work needed to be done for MQTT
    if gCanSend == True:
      sendDeviceProperty()


      ## TEST DATA
      if gCounter % 20 == 0:
        gCounter = 0
        print("Sending telemetry..")
        # The key in the json that will be sent to IoT Central must be equal to the Name in IoT Central!!
        # eg. "Temperature" in the json must equal the Name field "Temperature" in IoT Central
        # iotc.sendTelemetry("{ \
        # \"Temperature\": " + str(randint(0, 15)) + ", \
        # \"Pressure\": " + str(randint(850, 1150)) + ", \
        # \"Humidity\": " + str(randint(0, 100)) + ", \
        # \"CPUTemperature\": " + str(getCPUtemperature()) + ", \
        # }")
        iotc.sendTelemetry("{ \
                \"Temperature\": " + str(randint(0, 15)) + ", \
                \"Pressure\": " + str(randint(850, 1150)) + ", \
                \"Humidity\": " + str(randint(0, 100)) + ", \
                }")



      gCounter += 1

if __name__ == '__main__':
    main()