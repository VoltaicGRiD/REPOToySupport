# R.E.P.O. Toy Support

Have you ever wanted to be able use your Lovense toys with R.E.P.O.? 

No?

Well now you can!

This is a relatively simple implementation of the Lovense API that allows you to control your Lovense toys using R.E.P.O. and the Lovense Remote app.

There are a few different settings, allowing you to customize how the toys behave, based on your preferences.

## Installation

Ensure you have the latest (non-beta) version of R.E.P.O. installed, then follow these steps:
1. Download and install the following dependencies:
	- [BepInEx](https://thunderstore.io/c/repo/p/BepInEx/BepInExPack/) (if you don't have it already)
	- [MenuLib](https://thunderstore.io/c/repo/p/nickklmao/MenuLib/)
2. Download the latest release of this mod, from either the Thunderstore Mod Manager, or via "Manual Download" on the [Thunderstore page](https://thunderstore.io/c/repo/p/VoltaicGRiD/ToySupport)
	- If you downloaded the mod manually, extract the contents of the zip file into your `BepInEx/plugins` folder
3. Launch R.E.P.O. and wait for the mod to load
4. If you have "REPOConfig" installed, you can configure the mod from the "Mods" tab in game, otherwise, you can configure it by editing the `ToySupport.cfg` file in your `BepInEx/config` folder

## Settings / Configuration

"Vibration Based On" - This setting has three possible values:
- "value": will use the value of the grabbed object, scaled to the maximum value of all valuables in the level. [Default]
- "weight": will use the weight of the grabbed object, scaled to the maximum weight of all objects in the level.
- "impact": will use the impact severity of a collision while holding a valuable: light impacts = 'maxVibration' / 4, medium impacts = 'maxVibration' / 2, heavy impacts = 'maxVibration'.

"Max Vibration" - The maximum vibration strength that can be sent to the toys. This setting does not override the value in your Lovense app, and is instead, a percentage of that maximum. Value is out of 100%, clamped to 1. [Default: 1]

"Vibration Timeout" - The amount of time (in seconds) that the toys will vibrate for when a valuable is grabbed. [Default: 100]

"Vibrate on Hurt" - If enabled, the toys will vibrate when the player is hurt, based on the damage taken. [Default: true]

## How to connect your toys

1. Ensure you have the Lovense Remote app installed on your phone or tablet, and that your toys are connected to it.
2. Enable "Game Mode" in the Lovense Remote app, and note the IP address and SSL port displayed in the app.
3. In R.E.P.O., go to the "Settings" menu, then "Toy Support" (in the lower-left corner).
4. Enter the IP address and SSL port from the Lovense Remote, then click "Search".
5. After a moment, you should see a list of your connected toys. Select 'on' on the ones you want to use, the toy will vibrate momentarily to confirm connection.
6. Enjoy (or don't, if you've been consensually contracted to be miserable while playing with a friend or partner)

## Planned Features

- [ ] Add support for more toys (only able to test with Lush 3 and Gush 2)
- [ ] Add support for more settings (e.g. vibration strength, custom patterns, etc.)
- [ ] Implement an override to disable vibration if the vibration accidentially locks up, or if the toy is not responding
- [ ] Implement RPC support for controlling toys from other players
- [ ] Add support for enemies chasing the player, and vibrating the toys based on the distance to the player
- [ ] (Not sure) A shop upgrade to increase the maximum vibration strength, or to add integration with other aspects of the game (e.g. when using weapons, when an explosion goes off, etc.)
- [ ] Add a vibration mode based on the volume of the player's voice, and / or the volume of the environment around the player

## Bugs and Troubleshooting

Send a DM to `voltaicgrid` on Discord, or open an issue on the [GitHub repository](https://github.com/VoltaicGRiD/REPOToySupport)