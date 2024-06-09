## Changelog

### 1.5.0

- Added support for ``Streamer``.
- Added support for ``Infiltrator``.

### 1.4.0

- Added support for ``AnglerMimic``.

### 1.3.0

- Enemies mimicking voices now respects the volume slider for the player it mimics.
- Added a config entry ``MuteLocalPlayerVoice``.
    - When set to ``true``, enemies that mimic the local player cannot be heard until the player is dead (and spectating).
    - When set to ``false``, enemies that mimic the local player can always be heard.
    - Default value: ``false``.
- Added a config entry ``LocalPlayerVolume``.
    - A value between 0-1 that controls how loud enemies mimicking the local player should be.
    - This setting personal preference, and is not synced to other players.
    - Default value: ``1.0``.

### 1.2.2

- Fixed a performance issue that caused the game to stutter every couple seconds.

### 1.2.0

- Fixed a bug that caused enemies to not play audio when mimicking non-host players.

### 1.1.0

- Support v1.9.a of Content Warning.

### 1.0.1

- Add [MyceliumNetworking](RugbugRedfern-MyceliumNetworking-1.0.10) as a required dependency.

### 1.0.0

- Initial release.