﻿using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Asynchronous importer and loader
/// </summary>
namespace AsImpL
{

    /// <summary>
    /// Abstract loader to be used as a base class for specific loaders.
    /// </summary>
    public abstract class Loader : MonoBehaviour
    {
        /// <summary>
        /// Total loading progress, for all the models currently loading.
        /// </summary>
        public static LoadingProgress totalProgress = new LoadingProgress();

        /// <summary>
        /// Options to define how the model will be loaded and imported.
        /// </summary>
        public ImportOptions buildOptions;

#if UNITY_EDITOR
        /// <summary>
        /// Alternative texture path: if not null textures will be loaded from here.
        /// </summary>
        public string altTexPath = null;
#endif

        // raw subdivision in percentages of the loading phases (empirically computed loading a large sample OBJ file)
        // TODO: refine or change this method
        protected static float LOAD_PHASE_PERC = 8f;
        protected static float TEXTURE_PHASE_PERC = 1f;
        protected static float MATERIAL_PHASE_PERC = 1f;
        protected static float BUILD_PHASE_PERC = 90f;

        protected static Dictionary<string, GameObject> loadedModels = new Dictionary<string, GameObject>();
        protected static Dictionary<string, int> instanceCount = new Dictionary<string, int>();

        protected DataSet dataSet = new DataSet();
        protected ObjectBuilder objectBuilder = new ObjectBuilder();

        protected List<MaterialData> materialData;

        protected FileLoadingProgress objLoadingProgress = new FileLoadingProgress();

        protected float lastTime = 0;

        /// <summary>
        /// Load the file assuming its vertical axis is Z instead of Y 
        /// </summary>
        public bool ConvertVertAxis
        {
            get
            {
                return buildOptions != null ? buildOptions.zUp : false;
            }
            set
            {
                if (buildOptions == null)
                {
                    buildOptions = new ImportOptions();
                }
                buildOptions.zUp = value;
            }
        }


        /// <summary>
        /// Rescaling for the model (1 = no rescaling)
        /// </summary>
        public float Scaling
        {
            get
            {
                return buildOptions != null ? buildOptions.modelScaling : 1f;
            }
            set
            {
                if (buildOptions == null)
                {
                    buildOptions = new ImportOptions();
                }
                buildOptions.modelScaling = value;
            }
        }

        /// <summary>
        /// Check if a material library is defined for this model
        /// </summary>
        protected abstract bool HasMaterialLibrary { get; }

#if UNITY_EDITOR
        /// <summary>
        /// Import data as assets in the project (Editor only)
        /// </summary>
        public bool ImportingAssets { get { return !string.IsNullOrEmpty(altTexPath); } }
#endif

        /// <summary>
        /// Get a previusly loaded model by its absolute path
        /// </summary>
        /// <param name="absolutePath">absolute path used to load the model</param>
        /// <returns>The game object previously loaded</returns>
        public static GameObject GetModelByPath(string absolutePath)
        {
            if (loadedModels.ContainsKey(absolutePath))
            {
                return loadedModels[absolutePath];
            }
            return null;
        }


        /// <summary>
        /// Load an OBJ model.
        /// </summary>
        /// <param name="objName">name of the GameObject, if empty use file name</param>
        /// <param name="absolutePath">absolute file path</param>
        /// <param name="parentObj">Transform to which attach the loaded object (null=scene)</param>
        /// <returns>You can use StartCoroutine( loader.Load(...) )</returns>
        public IEnumerator Load(string objName, string absolutePath, Transform parentObj)
        {
            string fileName = Path.GetFileName(absolutePath);//absolutePath.Substring(absolutePath.LastIndexOf("/") + 1);
            string fileNameNoExt = Path.GetFileNameWithoutExtension(absolutePath);// fileName.IndexOf(".") == -1) ? fileName : fileName.Substring(0, fileName.LastIndexOf("."));
            string name = objName;
            if (name == null || name == "") objName = fileNameNoExt;

            totalProgress.fileProgress.Add(objLoadingProgress);
            objLoadingProgress.fileName = fileName;
            objLoadingProgress.error = false;
            objLoadingProgress.message = "Loading " + fileName + "...";
            lastTime = Time.realtimeSinceStartup;
            yield return null;
            // if the model was already loaded duplicate the existing object
            if (buildOptions!=null &&  buildOptions.reuseLoaded && loadedModels.ContainsKey(absolutePath) && loadedModels[absolutePath] != null)
            {
                Debug.LogFormat("File {0} already loaded, creating instance.", absolutePath);
                instanceCount[absolutePath]++;
                if (name == null || name == "") objName = objName + "_" + instanceCount[absolutePath];
                objLoadingProgress.message = "Instantiating " + objName + "...";
                while (loadedModels[absolutePath] == null)
                {
                    yield return null;
                }

                // TODO: option to reload the object

                GameObject newObj = Instantiate(loadedModels[absolutePath]);
                yield return newObj;
                newObj.name = objName;
                if (buildOptions != null)
                {
                    newObj.transform.localPosition = buildOptions.localPosition;
                    newObj.transform.localRotation = Quaternion.Euler(buildOptions.localEulerAngles); ;
                    newObj.transform.localScale = buildOptions.localScale;
                }

                if (parentObj != null) newObj.transform.parent = parentObj.transform;
                totalProgress.fileProgress.Remove(objLoadingProgress);
                yield break;
            }
            loadedModels[absolutePath] = null; // define a key for the dictionary
            instanceCount[absolutePath] = 0; // define a key for the dictionary

            yield return LoadModelFile(absolutePath);

            if (objLoadingProgress.error)
            {
                yield break;
            }
            lastTime = Time.realtimeSinceStartup;

            if (HasMaterialLibrary)
            {
                yield return LoadMaterialLibrary(absolutePath);
            }
            Debug.LogFormat("Material data parsed in {0} seconds", Time.realtimeSinceStartup - lastTime);
            lastTime = Time.realtimeSinceStartup;
            yield return Build(absolutePath, objName, parentObj);
            Debug.LogFormat("Game objects built in {0} seconds", Time.realtimeSinceStartup - lastTime);
            lastTime = Time.realtimeSinceStartup;
            Debug.LogFormat("Done ({0}).", objName);
            totalProgress.fileProgress.Remove(objLoadingProgress);
        }

        /// <summary>
        /// Parse the model to get a list of the paths of all used textures
        /// </summary>
        /// <param name="absolutePath">absolute path of the model</param>
        /// <returns>List of paths of the textures referenced by the model</returns>
        public abstract string[] ParseTexturePaths(string absolutePath);

        /// <summary>
        /// Load the main model file
        /// </summary>
        /// <param name="absolutePath">absolute file path</param>
        /// <remarks>This is called by Load() method</remarks>
        protected abstract IEnumerator LoadModelFile(string absolutePath);

        /// <summary>
        /// Load the material library from the given path.
        /// </summary>
        /// <param name="absolutePath">absolute file path</param>
        /// <remarks>This is called by Load() method</remarks>
        protected abstract IEnumerator LoadMaterialLibrary(string absolutePath);

        /// <summary>
        /// Build the game objects from data set, materials and textures.
        /// </summary>
        /// <param name="absolutePath">absolute file path</param>
        /// <param name="objName">Name of the main game object (model root)</param>
        /// <param name="parentTransform">transform to which the model root will be attached (if null it will be a root aobject)</param>
        /// <remarks>This is called by Load() method</remarks>
        protected IEnumerator Build(string absolutePath, string objName, Transform parentTransform)
        {
            float prevTime = Time.realtimeSinceStartup;
            WWW loader = null;
            if (materialData != null)
            {
                string basePath = GetDirName(absolutePath);
                objLoadingProgress.message = "Loading textures...";
                int count = 0;
                foreach (MaterialData mtl in materialData)
                {
                    objLoadingProgress.percentage = LOAD_PHASE_PERC + TEXTURE_PHASE_PERC * count / materialData.Count;
                    count++;
                    if (mtl.diffuseTexPath != null)
                    {
#if UNITY_EDITOR
                        if (ImportingAssets)
                        {
                            mtl.diffuseTex = LoadAssetTexture(mtl.diffuseTexPath);
                        }
                        else
#endif
                        {
                            //mtl.diffuseTex = TextureLoader.LoadTexture(basePath + mtl.diffuseTexPath);
                            string texPath = GetTextureUrl(basePath, mtl.diffuseTexPath);
                            loader = new WWW(texPath);
                            yield return loader;

                            if (loader.error != null)
                            {
                                Debug.LogError(loader.error);
                            }
                            else
                            {
                                mtl.diffuseTex = LoadTexture(loader);
                            }
                        }
                    }

                    if (mtl.bumpTexPath != null)
                    {
#if UNITY_EDITOR
                        if (ImportingAssets)
                        {
                            mtl.bumpTex = LoadAssetTexture(mtl.bumpTexPath);
                        }
                        else
#endif
                        {
                            //mtl.bumpTex = TextureLoader.LoadTexture(basePath + mtl.bumpTexPath);
                            string texPath = GetTextureUrl(basePath, mtl.bumpTexPath);
                            loader = new WWW(texPath);
                            yield return loader;

                            if (loader.error != null)
                            {
                                Debug.LogError(loader.error);
                            }
                            else
                            {
                                mtl.bumpTex = LoadTexture(loader);
                            }
                        }
                    }

                    if (mtl.specularTexPath != null)
                    {
#if UNITY_EDITOR
                        if (ImportingAssets)
                        {
                            mtl.specularTex = LoadAssetTexture(mtl.specularTexPath);
                        }
                        else
#endif
                        {
                            //mtl.specularTex = TextureLoader.LoadTexture(basePath + mtl.specularTexPath);
                            string texPath = GetTextureUrl(basePath, mtl.specularTexPath);
                            loader = new WWW(texPath);
                            yield return loader;

                            if (loader.error != null)
                            {
                                Debug.LogError(loader.error);
                            }
                            else
                            {
                                mtl.specularTex = LoadTexture(loader);
                            }
                        }
                    }

                    if (mtl.opacityTexPath != null)
                    {
#if UNITY_EDITOR
                        if (ImportingAssets)
                        {
                            mtl.opacityTex = LoadAssetTexture(mtl.opacityTexPath);
                        }
                        else
#endif
                        {
                            //mtl.opacityTex = TextureLoader.LoadTexture(basePath+mtl.opacityTexPath);
                            string texPath = GetTextureUrl(basePath, mtl.opacityTexPath);
                            loader = new WWW(texPath);
                            yield return loader;

                            if (loader.error != null)
                            {
                                Debug.LogError(loader.error);
                            }
                            else
                            {
                                mtl.opacityTex = LoadTexture(loader);
                            }
                        }
                    }
                }
            }
            Debug.LogFormat("Build-Textures loaded in {0} seconds", Time.realtimeSinceStartup - prevTime);
            prevTime = Time.realtimeSinceStartup;

            ObjectBuilder.ProgressInfo info = new ObjectBuilder.ProgressInfo();

            objLoadingProgress.message = "Loading materials...";
            yield return null;
#if UNITY_EDITOR
            objectBuilder.alternativeTexPath = altTexPath;
#endif
            objectBuilder.buildOptions = buildOptions;
            objectBuilder.InitBuilMaterials(materialData);
            float objInitPerc = objLoadingProgress.percentage;
            while (objectBuilder.BuildMaterials(info))
            {
                objLoadingProgress.percentage = objInitPerc + MATERIAL_PHASE_PERC * objectBuilder.NumImportedMaterials / materialData.Count;
                yield return null;
            }
            Debug.LogFormat("Build-Materials built in {0} seconds ({1})", Time.realtimeSinceStartup - prevTime, info.materialsLoaded);
            prevTime = Time.realtimeSinceStartup;

            objLoadingProgress.message = "Building scene objects...";

            GameObject newObj = new GameObject(objName);
            if (parentTransform != null) newObj.transform.SetParent(parentTransform.transform, false);
            ////newObj.transform.localScale = Vector3.one * Scaling;
            float initProgress = objLoadingProgress.percentage;
            objectBuilder.StartBuildObjectAsync(dataSet, newObj);
            while (objectBuilder.BuildObjectAsync(ref info))
            {
                objLoadingProgress.message = "Building scene objects... " + (info.objectsLoaded + info.groupsLoaded) + "/" + (dataSet.objectList.Count + info.numGroups);
                objLoadingProgress.percentage = initProgress + BUILD_PHASE_PERC * (info.objectsLoaded / dataSet.objectList.Count + (float)info.groupsLoaded / info.numGroups);
                yield return null;
            }
            objLoadingProgress.percentage = 100.0f;
            loadedModels[absolutePath] = newObj;
            Debug.LogFormat("Build-scene objects built in {0} seconds", Time.realtimeSinceStartup - prevTime);
        }

        /// <summary>
        /// Get the directory name of the given path, appending the final slash if eeded.
        /// </summary>
        /// <param name="absolutePath">the absolute path</param>
        /// <returns>the directory name ending with `/`</returns>
        protected string GetDirName(string absolutePath)
        {
            string dirName = Path.GetDirectoryName(absolutePath);
            string basePath = string.IsNullOrEmpty(dirName) ? "" : dirName;
            if (!basePath.EndsWith("/"))
            {
                basePath += "/";
            }
            return basePath;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Load a texture from the asset database
        /// </summary>
        /// <param name="texturePath">texture path inside the asset database</param>
        /// <returns>the loaded texture or null on error</returns>
        private Texture2D LoadAssetTexture(string texturePath)
        {
            FileInfo textFileInfo = new FileInfo(texturePath);
            string texpath = altTexPath + textFileInfo.Name;
            texpath = texpath.Replace("//", "/");
            Debug.LogFormat("Loading texture asset '{0}'", texpath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(texpath);
        }
#endif
        /// <summary>
        /// Convert a texture path to a texture URL and update the progress message
        /// </summary>
        /// <param name="basePath">base texture path</param>
        /// <param name="texturePath">relative texture path</param>
        /// <returns>URL of the texture</returns>
        private string GetTextureUrl(string basePath, string texturePath)
        {
            string texPath = basePath + texturePath;
            texPath = "file:///" + texPath.Replace("//", "/");
            objLoadingProgress.message = "Loading textures...\n" + texPath;
            //Debug.LogFormat("Loading texture '{0}'", texPath);
            return texPath;
        }

        /// <summary>
        /// Load a texture from a URL using a WWW
        /// </summary>
        /// <param name="loader">WWW object (already loaded)</param>
        /// <returns>The texturee loaded</returns>
        private Texture2D LoadTexture(WWW loader)
        {
            string ext = Path.GetExtension(loader.url).ToLower();
            Texture2D tex = null;

            // TODO: add support for more formats (bmp, gif, dds, ...)
            if (ext == ".tga")
            {
                tex = TextureLoader.LoadTexture(loader);
                //tex = TgaLoader.LoadTGA(new MemoryStream(loader.bytes));
            }
            else if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
            {
                tex = loader.texture;
            }
            else
            {
                Debug.LogWarning("Unsupported texture format: " + ext);
            }

            if (tex == null)
            {
                Debug.LogErrorFormat("Failed to load texture {0}", loader.url);
            }
            else
            {
                //tex.alphaIsTransparency = true;
                //tex.filterMode = FilterMode.Trilinear;
            }

            return tex;
        }
    }
}
