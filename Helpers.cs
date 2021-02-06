﻿using System;
using System.Collections;
using System.IO;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.VoiceNext;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace TheEpicAudioStreamer
{
    /// <summary>
    /// Provides static helper functions for the audio bot.
    /// </summary>
    static class Helpers
    {
        /// <summary>
        /// Prompts the user to select an audio playback device in the command console.
        /// </summary>
        /// <param name="deviceName">Optional: The friendly device name already preselected by the user.</param>
        /// <returns>The audio device, or null if no valid device was selected.</returns>
        public static MMDevice SelectDevice(string deviceName = "")
        {
            ArrayList devices = new ArrayList();
            var enumerator = new MMDeviceEnumerator();
            MMDevice audioDevice;
            int userDeviceID;

            // Populate ArrayList with Device IDs.
            foreach (var wasapi in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                devices.Add(wasapi.ID);
            }

            // If a preselected device name is given, use this device.
            if (deviceName != "")
            {
                foreach (string deviceID in devices)
                {
                    MMDevice device = enumerator.GetDevice(deviceID);
                    if (deviceName == device.DeviceFriendlyName)
                    {
                        // Device name found in list.
                        Console.WriteLine($"The device \"{device.FriendlyName}\" is being used in this session as per command line argument.\n");
                        audioDevice = device;
                        return audioDevice;
                    }
                }
                // A device name was given, but it is invalid.
                Console.WriteLine($"\"{deviceName}\" was given as a device name via command line argument, but it either does not exist or is unavailable.\n");
            }

            // Prompt user to select a readable device ID.
            Console.WriteLine("Please type the ID of the audio device you want to stream from:");
            foreach (string deviceID in devices)
            {
                MMDevice device = enumerator.GetDevice(deviceID);
                Console.WriteLine($"[{devices.IndexOf(device.ID)}] {device.DeviceFriendlyName} - {device.FriendlyName}");
            }
            Console.Write("> ");
            string userInput = Console.ReadLine();

            // Parse user input and get audio device via ID in the ArrayList.
            try
            {
                userDeviceID = int.Parse(userInput);
                audioDevice = enumerator.GetDevice(devices[userDeviceID].ToString());
            }
            catch (FormatException)
            {
                Console.WriteLine("Incorrect input. Integer ID expected.");
                return null;
            }
            catch (ArgumentOutOfRangeException)
            {
                Console.WriteLine("Not a valid device ID.");
                return null;
            }

            Console.WriteLine();
            return audioDevice;
        }

        /// <summary>
        /// Converts an IEEE Floating Point audio buffer into a 16bit PCM compatible buffer.
        /// </summary>
        /// <param name="inputBuffer">The buffer in IEEE Floating Point format.</param>
        /// <param name="length">The number of bytes in the buffer.</param>
        /// <param name="format">The WaveFormat of the buffer.</param>
        /// <returns>A byte array that represents the given buffer converted into PCM format.</returns>
        public static byte[] ToPCM16(byte[] inputBuffer, int length, WaveFormat format)
        {
            if (length == 0)
                return new byte[0]; // No bytes recorded, return empty array.

            // Create a WaveStream from the input buffer.
            using var memStream = new MemoryStream(inputBuffer, 0, length);
            using var inputStream = new RawSourceWaveStream(memStream, format);
            
            // Convert the input stream to a WaveProvider in 16bit PCM format with sample rate of 48000 Hz.
            var convertedPCM = new SampleToWaveProvider16(
                new WdlResamplingSampleProvider(
                    new WaveToSampleProvider(inputStream),
                    48000)
                );

            byte[] convertedBuffer = new byte[length];

            using var stream = new MemoryStream();
            int read;

            // Read the converted WaveProvider into a buffer and turn it into a Stream.
            while ((read = convertedPCM.Read(convertedBuffer, 0, length)) > 0)
                stream.Write(convertedBuffer, 0, read);

            // Return the converted Stream as a byte array.
            return stream.ToArray();
        }

        /// <summary>
        /// Generates an embed message.
        /// </summary>
        /// <param name="color">The color of the embed as a DiscordColor object.</param>
        /// <param name="title">The title of the embed.</param>
        /// <param name="description">The description of the embed.</param>
        /// <returns>The built DiscordEmbed object.</returns>
        public static DiscordEmbed GenerateEmbed(DiscordColor color, string title, string description)
        {
            var embed = new DiscordEmbedBuilder
            {
                Color = color,
                Title = title,
                Description = description
            };
            return embed.Build();
        }

        /// <summary>
        /// Generates an embed message.
        /// </summary>
        /// <param name="color">The color of the embed as a DiscordColor object.</param>
        /// <param name="description">The description of the embed.</param>
        /// <returns>The built DiscordEmbed object.</returns>
        public static DiscordEmbed GenerateEmbed(DiscordColor color, string description)
        {
            var embed = new DiscordEmbedBuilder
            {
                Color = color,
                Description = description
            };

            return embed.Build();
        }

        /// <summary>
        /// Checks whether the bot is connected to a voice channel.
        /// </summary>
        /// <param name="vnext">The current command context.</param>
        /// <returns>True if connected, false if not.</returns>
        public static bool CheckConnectionStatus(CommandContext ctx)
        {
            var connection = ctx.Client.GetVoiceNext().GetConnection(ctx.Guild);
            if (connection == null)
                return false;
            else
                return true;
        }

        /// <summary>
        /// Handles captured audio from a Wasapi device by converting it to PCM16 and writing it into a voice transmit sink.
        /// </summary>
        /// <param name="s">The sender object.</param>
        /// <param name="e">The event arguments.</param>
        /// <param name="sink">The Discord VoiceTransmitSink instance.</param>
        /// <param name="device">The WasapiLoopbackCapture device.</param>
        public static async void AudioDataAvilableEventHander(object s, WaveInEventArgs e, VoiceTransmitSink sink, WasapiLoopbackCapture device)
        {
            // If audio data is available, convert it into PCM16 format and write it into the stream.
            if (e.Buffer.Length > 0)
            {
                await sink.WriteAsync(ToPCM16(e.Buffer, e.BytesRecorded, device.WaveFormat));
            }
        }

        /// <summary>
        /// An event handler that prints potential error messages from the audio capture process to a Discord text channel.
        /// </summary>
        /// <param name="s">The sender object.</param>
        /// <param name="e">The event arguments.</param>
        /// <param name="ctx">The Discord Command Context.</param>
        public static async void AudioRecordingStoppedEventHandler(object s, StoppedEventArgs e, CommandContext ctx)
        {
            if (e.Exception != null)
                await ctx.RespondAsync(embed: GenerateEmbed(DiscordColor.Red, $"An error occured while capturing audio: '{e.Exception.Message}'"));
        }
    }
}
