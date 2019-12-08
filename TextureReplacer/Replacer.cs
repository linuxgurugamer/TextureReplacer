/*
 * Copyright © 2013-2019 Davorin Učakar
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 */

using System;
using System.Collections.Generic;
using KSP.UI.Screens.Flight;
using UnityEngine;

namespace TextureReplacer
{
  internal class Replacer
  {
    public const string DefaultPrefix = "TextureReplacer/Default/";
    public static readonly Shader EyeShader = Shader.Find("Standard");
    public static readonly Shader HeadShader = Shader.Find("Bumped Diffuse");

    private const string NavBall = "NavBall";
    private static readonly Vector2 NavBallScale = new Vector2(-1.0f, 1.0f);
    private static readonly Shader TexturedVisorShader = Shader.Find("KSP/Alpha/Translucent");

    private static readonly Log log = new Log(nameof(Replacer));

    // General texture replacements.
    private readonly Dictionary<string, Texture2D> mappedTextures = new Dictionary<string, Texture2D>();
    // NavBall texture.
    private Texture2D navBallTexture;
    // Change shinning quality.
    private SkinQuality skinningQuality = SkinQuality.Auto;
    // Print material/texture names when performing texture replacement pass.
    private bool logTextures;

    // Instance.
    public static Replacer Instance { get; private set; }

    public static void Recreate()
    {
      Instance = new Replacer();
    }

    /// <summary>
    /// Read configuration and perform pre-load initialisation.
    /// </summary>
    public void ReadConfig(ConfigNode rootNode)
    {
      Util.Parse(rootNode.GetValue("skinningQuality"), ref skinningQuality);
      Util.Parse(rootNode.GetValue("logTextures"), ref logTextures);
    }

    /// <summary>
    /// Post-load initialisation.
    /// </summary>
    public void Load()
    {
      if (skinningQuality != SkinQuality.Auto) {
        foreach (SkinnedMeshRenderer smr in Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>()) {
          smr.quality = skinningQuality;
        }
      }

      foreach (GameDatabase.TextureInfo texInfo in GameDatabase.Instance.databaseTexture) {
        Texture2D texture = texInfo.texture;
        if (texture == null || texture.name.Length == 0) {
          continue;
        }

        if (texture.filterMode == FilterMode.Bilinear) {
          texture.filterMode = FilterMode.Trilinear;
        }

        int defaultPrefixIndex = texture.name.IndexOf(DefaultPrefix, StringComparison.Ordinal);
        if (defaultPrefixIndex == -1)
          continue;

        string originalName = texture.name.Substring(defaultPrefixIndex + DefaultPrefix.Length);
        // Since we are merging multiple directories, we must expect conflicts.
        if (mappedTextures.ContainsKey(originalName))
          continue;

        if (originalName.StartsWith("GalaxyTex_", StringComparison.Ordinal)) {
          texture.wrapMode = TextureWrapMode.Clamp;
        }

        mappedTextures[originalName] = texture;
      }

      FixKerbalModels();

      // Find NavBall replacement textures if available.
      if (mappedTextures.TryGetValue(NavBall, out navBallTexture)) {
        mappedTextures.Remove(NavBall);

        if (navBallTexture.mipmapCount != 1) {
          log.Print("NavBall texture should not have mipmaps!");
        }
      }
    }

    public void OnBeginFlight()
    {
      if (navBallTexture != null) {
        UpdateNavBall();
      }
    }

    public void OnBeginScene()
    {
      ReplaceTextures();
    }

    /// <summary>
    /// General texture replacement step.
    /// </summary>
    private void ReplaceTextures()
    {
      foreach (Material material in Resources.FindObjectsOfTypeAll<Material>()) {
        if (!material.HasProperty(Util.MainTexProperty)) {
          continue;
        }

        Texture texture = material.mainTexture;
        if (texture == null || texture.name.Length == 0 || texture.name.StartsWith("Temp", StringComparison.Ordinal)) {
          continue;
        }

        if (logTextures) {
          log.Print("[{0}] {1}", material.name, texture.name);
        }

        mappedTextures.TryGetValue(texture.name, out Texture2D newTexture);

        if (newTexture != null && newTexture != texture) {
          newTexture.anisoLevel = texture.anisoLevel;
          newTexture.wrapMode = texture.wrapMode;

          material.mainTexture = newTexture;
          UnityEngine.Object.Destroy(texture);
        } else if (texture.filterMode == FilterMode.Bilinear) {
          texture.filterMode = FilterMode.Trilinear;
        }

        if (!material.HasProperty(Util.BumpMapProperty)) {
          continue;
        }

        Texture normalMap = material.GetTexture(Util.BumpMapProperty);
        if (normalMap == null) {
          continue;
        }

        mappedTextures.TryGetValue(normalMap.name, out Texture2D newNormalMap);

        if (newNormalMap != null && newNormalMap != normalMap) {
          newNormalMap.anisoLevel = normalMap.anisoLevel;
          newNormalMap.wrapMode = normalMap.wrapMode;

          material.SetTexture(Util.BumpMapProperty, newNormalMap);
          UnityEngine.Object.Destroy(normalMap);
        }
      }
    }

    /// <summary>
    /// Replace NavBalls' textures.
    /// </summary>
    private void UpdateNavBall()
    {
      if (navBallTexture == null)
        return;

      var hudNavBall = UnityEngine.Object.FindObjectOfType<NavBall>();
      if (hudNavBall != null) {
        Material material = hudNavBall.navBall.GetComponent<Renderer>().sharedMaterial;

        material.SetTexture(Util.MainTextureProperty, navBallTexture);
      }

      var ivaNavBall = InternalSpace.Instance.GetComponentInChildren<InternalNavBall>();
      if (ivaNavBall != null) {
        Material material = ivaNavBall.navBall.GetComponent<Renderer>().sharedMaterial;

        material.mainTexture = navBallTexture;
        material.SetTextureScale(Util.MainTexProperty, NavBallScale);
      }
    }

    private void FixKerbalModels()
    {
      mappedTextures.TryGetValue("eyeballLeft", out Texture2D eyeballLeft);
      mappedTextures.TryGetValue("eyeballRight", out Texture2D eyeballRight);
      mappedTextures.TryGetValue("pupilLeft", out Texture2D pupilLeft);
      mappedTextures.TryGetValue("pupilRight", out Texture2D pupilRight);
      mappedTextures.TryGetValue("kerbalVisor", out Texture2D ivaVisorTexture);
      mappedTextures.TryGetValue("EVAvisor", out Texture2D evaVisorTexture);

      // Shaders between male and female models are inconsistent, female models are missing normal maps and specular
      // lighting. So, we copy shaders from male materials to respective female materials.
      var loader = Prefab.Instance;

      SkinnedMeshRenderer[][] maleMeshes = {
        loader.MaleIva.GetComponentsInChildren<SkinnedMeshRenderer>(true),
        loader.MaleEva.GetComponentsInChildren<SkinnedMeshRenderer>(true),
        loader.MaleEvaVintage ? loader.MaleEvaVintage.GetComponentsInChildren<SkinnedMeshRenderer>(true) : null,
        loader.MaleEvaFuture ? loader.MaleEvaFuture.GetComponentsInChildren<SkinnedMeshRenderer>(true) : null
      };

      SkinnedMeshRenderer[][] femaleMeshes = {
        loader.FemaleIva.GetComponentsInChildren<SkinnedMeshRenderer>(true),
        loader.FemaleEva.GetComponentsInChildren<SkinnedMeshRenderer>(true),
        loader.FemaleEvaVintage ? loader.FemaleEvaVintage.GetComponentsInChildren<SkinnedMeshRenderer>(true) : null,
        loader.FemaleEvaFuture ? loader.FemaleEvaFuture.GetComponentsInChildren<SkinnedMeshRenderer>(true) : null
      };

      // Male materials to be copied to females to fix tons of female issues (missing normal maps, non-bump-mapped
      // shaders, missing teeth texture ...). There are also other inconsistencies in models, e.g. normal and vintage
      // Kerbals using different materials for eyes (specular vs. non-specular). We try to unify and fix the mess here.
      //
      // Note, though, Vintage and Modern IVA Kerbals are created on the fly, not cloned from prefabricated model, hence
      // we need to fix them each time, in Personaliser.PersonaliseKerbal().
      Material headMaterial = null;
      Material[] visorMaterials = {null, null, null};

      for (int i = 0; i < 3; ++i) {
        if (maleMeshes[i] == null) {
          continue;
        }

        foreach (SkinnedMeshRenderer smr in maleMeshes[i]) {
          // Many meshes share the same material, so it suffices to perform fixes only on one mesh for each material.
          Material sharedMaterial = smr.sharedMaterial;

          switch (smr.name) {
            case "eyeballLeft": {
              sharedMaterial.shader = EyeShader;
              sharedMaterial.mainTexture = eyeballLeft;
              break;
            }
            case "eyeballRight": {
              sharedMaterial.shader = EyeShader;
              sharedMaterial.mainTexture = eyeballRight;
              break;
            }
            case "pupilLeft": {
              sharedMaterial.shader = EyeShader;
              sharedMaterial.mainTexture = pupilLeft;
              if (pupilLeft != null) {
                sharedMaterial.color = Color.white;
              }

              break;
            }
            case "pupilRight": {
              sharedMaterial.shader = EyeShader;
              sharedMaterial.mainTexture = pupilRight;
              if (pupilRight != null) {
                sharedMaterial.color = Color.white;
              }

              break;
            }
            case "headMesh01":
            case "headMesh02": {
              // Replace with bump-mapped shader so normal maps for heads will work.
              sharedMaterial.shader = HeadShader;
              headMaterial = sharedMaterial;
              break;
            }
            case "visor": {
              // It will be replaced with reflective shader later, if reflections are enabled.
              switch (i) {
                case 0: { // maleIva
                  if (ivaVisorTexture != null) {
                    sharedMaterial.shader = TexturedVisorShader;
                    sharedMaterial.mainTexture = ivaVisorTexture;
                    sharedMaterial.color = Color.white;
                  }

                  break;
                }
                case 1: { // maleEva
                  if (evaVisorTexture != null) {
                    sharedMaterial.shader = TexturedVisorShader;
                    sharedMaterial.mainTexture = evaVisorTexture;
                    sharedMaterial.color = Color.white;
                  }

                  break;
                }
                case 2: { // maleEvaVintage
                  smr.sharedMaterial = visorMaterials[1];
                  break;
                }
                case 3: { // maleEvaFuture
                  break;
                }
              }

              visorMaterials[i] = sharedMaterial;
              break;
            }
          }
        }
      }

      for (int i = 0; i < 3; ++i) {
        if (femaleMeshes[i] == null) {
          continue;
        }

        foreach (SkinnedMeshRenderer smr in femaleMeshes[i]) {
          if (smr.sharedMaterial == null) {
            continue;
          }

          // Here we must enumerate all meshes wherever we are replacing the material.
          Material sharedMaterial = smr.sharedMaterial;
          switch (smr.name) {
            case "mesh_female_kerbalAstronaut01_kerbalGirl_mesh_eyeballLeft": {
              sharedMaterial.shader = EyeShader;
              sharedMaterial.mainTexture = eyeballLeft;
              break;
            }
            case "mesh_female_kerbalAstronaut01_kerbalGirl_mesh_eyeballRight": {
              sharedMaterial.shader = EyeShader;
              sharedMaterial.mainTexture = eyeballRight;
              break;
            }
            case "mesh_female_kerbalAstronaut01_kerbalGirl_mesh_pupilLeft": {
              sharedMaterial.shader = EyeShader;
              sharedMaterial.mainTexture = pupilLeft;
              if (pupilLeft != null) {
                sharedMaterial.color = Color.white;
              }

              break;
            }
            case "mesh_female_kerbalAstronaut01_kerbalGirl_mesh_pupilRight": {
              sharedMaterial.shader = EyeShader;
              sharedMaterial.mainTexture = pupilRight;
              if (pupilRight != null) {
                sharedMaterial.color = Color.white;
              }

              break;
            }
            case "mesh_female_kerbalAstronaut01_kerbalGirl_mesh_pCube1":
            case "mesh_female_kerbalAstronaut01_kerbalGirl_mesh_polySurface51": {
              // Replace with bump-mapped shader so normal maps for heads will work.
              sharedMaterial.shader = HeadShader;
              break;
            }
            case "mesh_female_kerbalAstronaut01_kerbalGirl_mesh_upTeeth01":
            case "mesh_female_kerbalAstronaut01_kerbalGirl_mesh_downTeeth01": {
              // Females don't have textured teeth, they use the same material as for the eyeballs. Extending female
              // head material/texture to their teeth is not possible since teeth overlap with some ponytail subtexture.
              // However, female teeth map to the same texture coordinates as male teeth, so we fix this by applying
              // male head & teeth material for female teeth.
              smr.sharedMaterial = headMaterial;
              break;
            }
            case "visor":
            case "mesh_female_kerbalAstronaut01_visor": {
              smr.sharedMaterial = visorMaterials[i];
              break;
            }
          }
        }
      }
    }
  }
}
