"""Creates the Unity side of the Christmas presents, only where it is missing (GUIDs stay stable on re-runs):
  Assets/Resources/ChristmasPresents.png.meta           texture import settings (uncompressed, no mips, clamp)
  Assets/Resources/ChristmasPresentsConfig.json.meta
  Assets/Material/ChristmasPresents.mat (+ .meta)       Unlit/Transparent with the atlas
  Assets/Scripts/Assembly-CSharp/ChristmasPresentView.cs.meta
  Assets/Resources/Prefabs/0_Generic/4_ChristmasPresents/ChristmasPresent_<Name>.prefab (+ .meta), one per present
Run after presents.py: python3 -B ArtSource/ChristmasPresents/make_unity_assets.py
"""
import json
import os
import re
import uuid

ROOT = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(ROOT, "..", ".."))
A = lambda *p: os.path.join(PROJECT, "Assets", *p)

SHADOW_TEXTURE_GUID = "f1cc06cc75af4863b5301ddb2d313b29"  # SwapCrystal_Shadow.png
TEXTURE_META_TEMPLATE = A("Texture2D", "SwapCrystal_Glow.png.meta")
PREFAB_DIR = A("Resources", "Prefabs", "0_Generic", "4_ChristmasPresents")


def write_new(path, text, crlf=False):
    if os.path.exists(path):
        return False
    with open(path, "w", newline="\r\n" if crlf else "\n") as f:
        f.write(text)
    print("wrote", os.path.relpath(path, PROJECT))
    return True


def guid_of(meta_path):
    with open(meta_path) as f:
        return re.search(r"guid: ([0-9a-f]{32})", f.read()).group(1)


def ensure_meta(asset_path, body):
    meta = asset_path + ".meta"
    write_new(meta, "fileFormatVersion: 2\nguid: %s\n%s" % (uuid.uuid4().hex, body))
    return guid_of(meta)


def main():
    # texture
    with open(TEXTURE_META_TEMPLATE) as f:
        tm = f.read()
    tm = re.sub(r"guid: [0-9a-f]{32}", "guid: " + uuid.uuid4().hex, tm, count=1)
    tm = re.sub(r"(\n    textureCompression:) \d+", r"\1 0", tm)
    tm = re.sub(r"(\n  isReadable:) \d+", r"\1 0", tm)
    write_new(A("Resources", "ChristmasPresents.png.meta"), tm)
    tex_guid = guid_of(A("Resources", "ChristmasPresents.png.meta"))

    ensure_meta(A("Resources", "ChristmasPresentsConfig.json"),
                "TextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
    script_guid = ensure_meta(A("Scripts", "Assembly-CSharp", "ChristmasPresentView.cs"),
                              "MonoImporter:\n  serializedVersion: 2\n  name:\n  defaultReferences: []\n"
                              "  executionOrder: 0\n  icon: {fileID: 0}\n  userData:\n  assetBundleName:\n"
                              "  assetBundleVariant:\n")

    mat = A("Material", "ChristmasPresents.mat")
    write_new(mat, """%%YAML 1.1
%%TAG !u! tag:unity3d.com,2011:
--- !u!21 &2100000
Material:
  serializedVersion: 6
  m_ObjectHideFlags: 0
  m_PrefabParentObject: {fileID: 0}
  m_PrefabInternal: {fileID: 0}
  m_Name: ChristmasPresents
  m_Shader: {fileID: 10750, guid: 0000000000000000f000000000000000, type: 0}
  m_ShaderKeywords:
  m_LightmapFlags: 4
  m_EnableInstancingVariants: 0
  m_DoubleSidedGI: 0
  m_CustomRenderQueue: -1
  stringTagMap: {}
  disabledShaderPasses: []
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs:
    - _MainTex:
        m_Texture: {fileID: 2800000, guid: %s, type: 3}
        m_Scale: {x: 1, y: 1}
        m_Offset: {x: 0, y: 0}
    m_Floats: []
    m_Colors: []
""" % tex_guid)
    mat_guid = ensure_meta(mat, "NativeFormatImporter:\n  name:\n  mainObjectFileID: 2100000\n  userData:\n"
                                "  assetBundleName:\n  assetBundleVariant:\n")

    os.makedirs(PREFAB_DIR, exist_ok=True)
    if not os.path.exists(PREFAB_DIR + ".meta"):
        write_new(PREFAB_DIR + ".meta", "fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n"
                                        "  userData:\n  assetBundleName:\n  assetBundleVariant:\n" % uuid.uuid4().hex)

    with open(A("Resources", "ChristmasPresentsConfig.json")) as f:
        names = [p["name"] for p in json.load(f)["presents"]]
    for name in names:
        prefab = os.path.join(PREFAB_DIR, "ChristmasPresent_%s.prefab" % name)
        write_new(prefab, """%%YAML 1.1
%%TAG !u! tag:unity3d.com,2011:
--- !u!1 &1100000000000001
GameObject:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  serializedVersion: 6
  m_Component:
  - component: {fileID: 400000000000001}
  - component: {fileID: 11400000000000001}
  m_Layer: 9
  m_Name: ChristmasPresent_%(name)s
  m_TagString: Untagged
  m_Icon: {fileID: 0}
  m_NavMeshLayer: 0
  m_StaticEditorFlags: 0
  m_IsActive: 1
--- !u!4 &400000000000001
Transform:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 1100000000000001}
  serializedVersion: 2
  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_LocalScale: {x: 1, y: 1, z: 1}
  m_ConstrainProportionsScale: 0
  m_Children: []
  m_Father: {fileID: 0}
  m_LocalEulerAnglesHint: {x: 0, y: 0, z: 0}
--- !u!114 &11400000000000001
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 1100000000000001}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: %(script)s, type: 3}
  m_Name:
  m_EditorClassIdentifier:
  m_PresentName: %(name)s
  m_Material: {fileID: 2100000, guid: %(mat)s, type: 2}
  m_ShadowTexture: {fileID: 2800000, guid: %(shadow)s, type: 3}
""" % dict(name=name, script=script_guid, mat=mat_guid, shadow=SHADOW_TEXTURE_GUID))
        ensure_meta(prefab, "PrefabImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n"
                            "  assetBundleVariant: \n")


if __name__ == "__main__":
    main()
