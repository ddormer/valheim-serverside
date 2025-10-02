## [1.1.9] - 2025-05-14

### Fixed

- Fix missing effects (Revert EffectList.Create patch)


## [1.1.8] - 2025-04-20

### Added

- Remove `AudioMan.Update`, reducing future error spam


### Fixed

- Fix null references to `WearNTear.m_bounds` and `Humanoid.m_currentAttack.m_character`
- Fix shield generators and audio log spam


## [1.1.7] - 2024-11-12

### Added

- Environment.props and bepinex publicizer


### Fixed

- Update method references to static, fixing Bog Witch launch issue. Thanks to @bpage-dev


## [1.1.6] - 2023-11-22

### Fixed

- Fix exception when updating ship owner in 0.217.28
- Fix MaxObjectsPerFrame transpiler for Valheim 0.217.28


## [1.1.5] - 2023-10-25

### Fixed

- Fix private method access errors in Release build


## [1.1.4] - 2023-10-24

### Changed

- Mistlands update


### Misc

- Fix AssemblyPublicizer output path.
- Update BepInEx, Harmony and MonoMod libs


## [1.1.4] - 2023-10-24

### Changed

- Mistlands update


## [1.1.3] - 2021-09-22

### Fixed

- Fix Valheim Plus autofuel compatibility.


## [1.1.2] - 2021-09-16

### Changed

- Remove old fishing fixes (appears to have been fixed in latest Valheim patch)


## [1.1.1] - 2021-05-14

### Changed

- Update to BepInEx 5.4.10


### Fixed

- Fix fishing


## [1.1.0] - 2021-05-07

### Added

- Added "max objects per frame" configuration. Allowing for faster or slower area loading on the server.


## [1.0.3] - 2021-04-21

### Fixed

- Fix Ship container access and improve Ship ownership transfer
- Fix Ship taking 10 damage when owner changes to server.


## [1.0.2] - 2021-04-19

### Fixed

- Fix objects not being created on the server in 0.150.3.


## [1.0.1] - 2021-04-15

### Fixed

- Prevent event monsters from spawning outside of the random event area, during a random event.
