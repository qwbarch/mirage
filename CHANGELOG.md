## Changelog

### 1.8.0

- Added support for the following v56 enemies:
    - Clay Surgeon
    - Bush Wolf

### 1.7.2

- Added a config option to set the maximum number of naturally spawned masked enemies. By default, this is set to 2.
  Masked enemy spawns have already been capped at 2 since ``v1.7.1``, this update simply allows you to adjust that value.

### 1.7.1

- Fixed a bug that caused non-ascii file paths to fail to load. This means players with non-english usernames will finally be able to hear their own voices.

### 1.7.0

- Added a config entry ``RecordWhileDead``, that continues to record a player's voice while they're dead. Default value: ``false``.
- Fixed a bug that caused monsters to try to mimic disconnected players (resulting in no sound, since players must be connected in order to send their voices to others).

### 1.6.1

- Added a config entry ``UseCustomSpawnCurve``, that when enabled, changes masked enemies to spawn later in the day.
    - Requires ``EnableOverrideSpawnChance = true`` in order to work.
    - Credits: [TheDebbyCase](https://thunderstore.io/c/lethal-company/p/deB/) for explaining in-depth how spawn curves work.

### 1.6.0

- Fixed a bug that caused player dead bodies to disappaer.

### 1.5.4

- Temporarily reverted back to ``v1.5.2`` due to a new bug introduced in ``v1.5.3``.

### 1.5.3

- ~~Fixed a bug that caused player dead bodies to disappear.~~

### 1.5.2

- Voice clips are no longer created when [ToggleMute](https://thunderstore.io/c/lethal-company/p/quackandcheese/ToggleMute/) is toggled.

### 1.5.1

- Audio spatializer warning logs are now hidden.
    - This is a vanilla bug that occured more often with Mirage, due to cloning audio sources used by the vanilla game.
    - This log does not provide any value when debugging issues, so I opted to hide them instead.
    - Thanks to [IAmBatby](https://github.com/IAmBatby) and [mattymatty](https://github.com/mattymatty97) for their help/implementation.

### 1.5.0

- Fixed ``LocalPlayerVolume`` being accidentally synced. Each player can now control the volume of their mimicked voices, as originally intended.
- Fixed a bug that caused the ghost girl to mimic voices even with ``EnableGhostGirl`` set to ``false``.
- Fixed a bug that caused the ghost girl to mimic voices at the wrong timings.
- Recordings now also delete on game startup (previously only deleted when closing the game), while ``IgnoreRecordingsDeletion`` is set to ``false``.
- Changed the way masked enemies disable the arms-out animation to hopefully get rid of the rare issue of it having its arms-out when it's not supposed to.

### 1.4.0

- Added a config entry for the new enemies (butler, butler bees, flower snake, old bird).
- Removed debug logs that I previously left in by accident in ``v1.3.2``.
- Fixed a bug that caused modded enemies that extends from a vanilla enemy type to share its config entry.
- Fixed a bug that potentially caused an out-of-bounds error if a player dc'd when an enemy tries to mimic a player.

### 1.3.2

- Fixed a bug introduced in ``v1.3.0``, where some players didn't have their recordings folder deleted upon closing the game.
- Fixed a bug introduced in ``v1.3.1``, where voice clips sometimes get repeated multiple times in a row.

### 1.3.1

- Enemies mimicking a player now picks at a slightly less random way to avoid mimicking the same player multiple times in a row.
- ``MuteLocalPlayerVoice`` is now set to ``false`` by default, due to users who are new to the mod being confused why they can't hear their mimicked voice.

### 1.3.0

Note: This update removes a couple config entries in favour of new ones. You can safely ignore any orphaned config entries.

- Removed the config entry ``AlwaysMuteLocalPlayer`` in favour of ``LocalPlayerVolume``.
- Removed the config entry ``EnableNaturalSpawn`` as it only made sense to have in the past.
- Added the config entry ``LocalPlayerVolume``.
    - Allows you to adjust the playback volume for voice mimics of the local player.
    - Value must be between 0-1.
    - Default is set to 1.
    - This config entry is not synced to other players, as it's personal preference on what volume you want for your own "mimics".
- Added the config entry ``EnableOverrideSpawnChance``, which allows you to enable/disable the ``OverrideSpawnChance`` config.
    - Default is set to ``true``.
    - If you want to use a different spawn control mod (such as [LethalQuantities](https://thunderstore.io/c/lethal-company/p/BananaPuncher714/LethalQuantities/)), simply set this to ``false``.
- Added the config entry ``OverrideSpawnChance``, allowing you to control how often masked enemies should spawn.
    - Value must be between 0-100 (as it's a percentage).
    - Default is set to 15.
    - Not to be confused with spawn weights. This will internally calculate the spawn weights for each moon to fit the desired percentage.
    - This will overwrite the spawn weights set by [LethalQuantities](https://thunderstore.io/c/lethal-company/p/BananaPuncher714/LethalQuantities/) and [LethalLevelLoader](https://thunderstore.io/c/lethal-company/p/IAmBatby/LethalLevelLoader/). If you want to use those mods for spawn control, set ``EnableOverrideSpawnChance`` to ``false``.
    - Masked enemies spawn this way is capped at 2 (this counter is not shared with the ``SpawnOnPlayerDeath`` feature).
- Mimicking voices should *actually* sound like real player voices now.
    - This was previously attempted by imitating all the settings/filters that dissonance uses.
    - This is now replaced by using what dissonance uses internally.
- Enemies that mimic non-player voices now adjust playback volume, if the mimicking player's volume has been adjusted.
- Re-added support for [LobbyCompatibility](https://thunderstore.io/c/lethal-company/p/BMX/LobbyCompatibility/).
- ``ImitationMode`` is now set to ``NoRepeat`` by default, to provide a better experience right out of the box.

### 1.2.1

- Temporarily removed support for [LobbyCompatibility](https://thunderstore.io/c/lethal-company/p/BMX/LobbyCompatibility/). It currently has a bug that makes it a required dependency, causing Mirage to error when starting/joining a lobby, if you don't have it installed.

### 1.2.0

- Add support for [LobbyCompatibility](https://thunderstore.io/c/lethal-company/p/BMX/LobbyCompatibility/)
    - Compatibility level is set to minor version. E.g. any ``1.2.X`` lobby is compatible with any other ``1.2.X`` version of Mirage.
    - This only applies to ``v1.2.X`` and forward, as I did not follow proper versioning in the ``1.0.X`` versions.
- Fixed issues of players not being able to hear voices mimicking them at times.

### 1.1.1

- Add configuration option ``ImitateMode`` to change how a recording is picked.
    - Set this to ``Random`` to keep the current behaviour (recordings are picked completely random).
    - Set this to ``NoRepeat`` to avoid repeating the same recording (this can still happen when not many recordings exist). Here is how it works:
        - A pool of recordings is filled.
        - A recording is randomly picked, and then removed from the pool.
        - When pool becomes empty, it becomes filled again.
- ``SpawnOnPlayerDeath`` no longer spawns an enemy if the player is not on a navmesh.
    - Previously, this was band-aid fixed by preventing a spawn if the player died by gravity.
    - ``CauseOfDeath: Gravity`` no longer prevents the masked enemy from spawning, meaning ladders can spawn them properly now.
    - Note: Many areas throughout the map such as standing on top of a railing is detected as a missing navmesh.
      Such areas will prevent ``SpawnOnPlayerDeath`` from spawning a masked enemy.

### 1.1.0

- Rewrote the config syncing logic. Thanks to Owen3H (author of CSync) for their config syncing [implementation](https://gist.github.com/Owen3H/c73e09314ed71b254256cbb15fd8c51e/5f314116ccd2ba3e5a2a38f01cf889dc674f2cfa).
- ``DeleteRecordingsPerRound`` has been slightly reworked.
    - Setting this to ``true`` is unchanged (recordings get deleted after the lever is pulled, per round).
    - Setting this to ``false`` now only deletes when closing the game.
    - Default value is now set to ``false``.
- ``MuteLocalPlayerVoice`` is now set to ``true`` as the default.
- ``EnablePenalty`` is now set to ``true`` as the default.
    - To clarify, setting this to ``true`` is the vanilla behaviour.
    - This will likely be removed in a future update, as it's a relic of the past back when Mirage had different goals.

### 1.0.16

- Fixed a bug that caused the ship teleporter to not work if ``SpawnOnPlayerDeath`` spawned a masked enemy
- [DissonanceLagFix](https://thunderstore.io/c/lethal-company/p/linkoid/DissonanceLagFix/) is no longer needed when using Mirage
- Modify configuration descriptions to hopefully be easier to understand
- Modify configuration default values

### 1.0.15

- Fixed a mod conflict issue that caused enemies to not despawn properly
- ``SpawnOnPlayerDeath`` no longer spawns an enemy when ``CauseOfDeath = Gravity`` (this means dying to fall damage, and I believe dying to the ladder as well)
    - This was originally done by **jarylc**, but I tried alternatives to avoid having the mechanic completely ignore dying to gravity
    - This is probably the best band-aid fix for now for avoiding log spam when a masked enemy spawns inside a pit

Huge thanks to **TheDebbyCase** for their immense effort on bug-testing and figuring out a way to reproduce errors that others have been experiencing.
Thanks to **dinogolfer** from TMLC for figuring out why non-masked enemies weren't despawning as well, which helped me understand why this mod conflict was even happening.

### 1.0.14

- Fixed a bug where some enemies types didn't mimic voices
- Add configuration option to always mute the mimicking voice of a local player
    - Set ``AlwaysMuteLocalPlayer`` to true to never hear your voice at all (even while dead)
    - This ignores the ``MuteLocalPlayerVoice`` value if enabled
    - This value is not synced to all clients, as it's an optional feature for those who don't want to hear their own voice

### 1.0.13

- AdvancedCompany cosmetics now properly apply on hosts (previously only applied on non-hosts)
- Fixed a bug that caused ``MuteLocalPlayerVoice`` to get ignored

### 1.0.12

- Mimicked voices should now sound a lot more like an actual player's voice (changed audio filters)
- ``SpawnOnPlayerDeath`` no longer spams an error if a player falls into the void
- Masked enemies no longer mimic voices if it was spawned after a player falls into the void

### 1.0.11

- Hotfix: Revert the crossed-out change from v1.0.10, which caused non-host players to not hear any voices

### 1.0.10

- Fixed dress girl issues
    - Mimicking voice is now muted while the dress girl is invisible
    - In singleplayer, dress girl will always mimic the local player's voice
    - In multiplayer, dress girl will always mimic the non-local player's voice
- Bees no longer mimic voices when ``EnableModdedEnemies`` is enabled.
- Add configuration option to mimic voices for locust swarms
- ~~Fixed navmesh error spam when an enemy tries to calculate its path~~
- Recordings deletion can now be ignored (not synced to all players)

### 1.0.9

- Ship camera now spectates the correct masked enemy
- Recording deletion can now be configured in two ways:
    - ``DeleteRecordingsPerRound = true`` to delete recordings per-round
    - ``DeleteRecordingsPerRound = false`` to delete recordings per-game
- Recordings now automatically delete upon closing the game

### 1.0.8

- Fixed a bug where voice filters weren't being applied, causing mimicked voices to behave unpredictably
- Fixed a bug where **SpawnOnPlayerDeath** could spawn two masked enemies in certain scenarios

### 1.0.7

- Hotfix: If the local player's mimicking voice is muted, it now becomes unmuted when the player is dead

### 1.0.6

Thanks to [jarylc](https://github.com/jarylc) for the initial [fixes](https://gist.github.com/jarylc/3f6fc305466d4970deae16629469b3c2) for masked enemy spawns.

- Rewrite masked enemy spawn and mimicking logic
    - This is less invasive now and should conflict with other mods less often
    - Fixed a bug where naturally spawned masked enemies weren't synced properly
- Fixed a bug where players not haunted by the ghost girl could hear the mimicking voice.
- Add configuration option to only spawn player on death, when the dying player is alone
- Add configuration option to mute the mimicking voice when mimicking the local player
- Add configuration option to adjust voice mimicking delay for non-masked enemies
- Add configuration option to mimic voices on modded enemies

### 1.0.5

- Use audio filters to sound more like vanilla voices
- Mimicked voices on the outside can no longer be heard from inside the facility (and vice-versa)
- Masked enemies no longer mimic voices while hiding on the ship

### 1.0.4

- Support voice mimicking on all vanilla enemies.
- Add configuration option for mask texture and arms-out animation (for masked enemies).
- Dead enemies no longer mimic voices.

### 1.0.3

- Support voice activity.

### 1.0.2

- Bundled all dependencies I control into a single core lib (users now pull less packages).

### 1.0.1

- Spawn on player death is now configurable.
- Naturally spawned masked enemies now mimic a player at random.
- Dependencies are now separated into its own packages.

### 1.0.0

- Initial release.
