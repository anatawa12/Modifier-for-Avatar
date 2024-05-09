# Modifier for Avatar

Set of anatawa12's NDMF-based small Avatar Modification tools.

## Installation

Currently, this package is not planned to release stable version.
So, please clone this repository to your project OR use git URL in Package Manager.

## Components

### M4A Make Children

This is replacement of [AAO Make Children].

[AAO Make Children]: https://vpm.anatawa12.com/avatar-optimizer/ja/docs/reference/make-children/

### M4A Deformer

This component is to deform the avatar with scale, rotation and translation.
This component is intended to make chibi variant of the avatar.

You can configure how deforms the avatar by setting `DeformInfo` asset.

See [this note](https://misskey.niri.la/notes/9m1k9slxs5)

#### DeformInfo

The asset file to configure how deforms the avatar.
You can create this asset from `Create/M4A Deform Info`

Simple way to configure this is to import deformed avatar.
Set the GameObject to `Import` and click `Import from GameObject`.

### M4A Generate Remove Eye Blend Shape

This component is to generate BlendShape that removes eye from BlendShape for something like `> <`.

This component is intended be used with `M4A Add Manga Expression BlendShape`

### M4A Add Manga Expression BlendShape

This component is to add polygons to avatar to make manga-like expression.

Not implemented

