# Changelog

## [0.3.1]

### Changed

- Made the `VisEquipment.AttachItem` transpiler locate its selection hook semantically.
- Kept instantiation-shape validation as a non-blocking Debug diagnostic.
- Kept Valheim's original `Object.Instantiate` instruction intact for better transpiler interoperability.
- Made temporary visual-source cleanup scope-based and safe for nested `AttachItem` calls.
- Preserved nested `ZNetView.m_forceDisableInit` state with save/restore semantics.
- Excluded diagnostic logging and strings from Release builds.
- Separated hand control, attach-source selection, and drop patching into focused classes.

## [0.3.0]

### Changed

- Removed the Jotunn/JVL dependency.
- Removed the EquipmentAndQuickSlots dependency.
- Replaced quick-slot integration with Alt-click controls in the vanilla player inventory.
- Added a configurable local hand-selection modifier, defaulting to Left Alt.

## [0.2.2]

### Fixed

- Updated visual equipment integration for Valheim 1.0.
- Prevented a single owned item from appearing in both hands.
- Removed hand visuals when dropping items leaves too few copies in the inventory.
- Added prefab-root support for multi-part item visuals.
