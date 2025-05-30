﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using Modding;
using SFCore.Generics;
using SFCore.Utils;
using SvgLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UScene = UnityEngine.SceneManagement.Scene;
using UObject = UnityEngine.Object;

namespace ScenePainter;

public class ScenePainterGlobalSettings
{
    public string EmptyColor = "#FFFFFF";
    public string WallColor = "#000000";
    public string DamageColor = "#FF0000";
    // public string MiscColor = "#0000FF";
}

public class ScenePainter : GlobalSettingsMod<ScenePainterGlobalSettings>
{
    private static string _dir;
    private static string _folder = "ScenePainter";
    private static bool _shouldDump = true;

    public override List<ValueTuple<string, string>> GetPreloadNames()
    {
        var dict = new List<ValueTuple<string, string>>();
        int max = UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < max; i++)
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
            dict.Add((Path.GetFileNameWithoutExtension(scenePath), "_SceneManager"));
        }

        return dict;
    }

    public ScenePainter() : base("Scene Painter")
    {
        _dir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? throw new DirectoryNotFoundException("I have no idea how you did this, but good luck figuring it out."), _folder);
        if (!Directory.Exists(_dir))
        {
            Directory.CreateDirectory(_dir);
        }

        UnityEngine.SceneManagement.SceneManager.sceneLoaded += SceneManagerOnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.activeSceneChanged += SceneManagerOnactiveSceneChanged;
    }

    private void SceneManagerOnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
    {
        if (!_shouldDump) return;
        var (success, reason) = MakeTextureFromScene(scene);
        if (success)
        {
            Log($"Added Texture for scene \"{scene.name}\"!");
        }
        else
        {
            Log($"ERROR: Couldn't paint scene \"{scene.name}\": {reason}");
        }
    }

    private void SceneManagerOnactiveSceneChanged(Scene from, Scene to)
    {
        if (!_shouldDump) return;
        var (success, reason) = MakeTextureFromScene(to);
        if (success)
        {
            Log($"Added Texture for scene \"{to.name}\"!");
        }
        else
        {
            Log($"ERROR: Couldn't paint scene \"{to.name}\": {reason}");
        }
    }

    public override void Initialize()
    {
        _shouldDump = false;
        GameManager.instance.StartCoroutine(DumpCurrentScene());
    }

    private IEnumerator DumpCurrentScene()
    {
        while (true)
        {
            yield return new WaitWhile(() => !Input.GetKeyDown(KeyCode.K));

            UScene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var (success, reason) = MakeTextureFromScene(scene);
            if (success)
            {
                Log($"Added Texture for scene \"{scene.name}\"!");
            }
            else
            {
                Log($"ERROR: Couldn't paint scene \"{scene.name}\": {reason}");
            }
        }
    }

    private (bool, string) MakeTextureFromScene(UScene scene)
    {
        string[] possibleNamesOfTileMapGameObject = new[]
        {
            $"{scene.name}-TileMap",
            "TileMap",
            "Template-TileMap",
        };
        GameObject tileMapGo = null;
        foreach (string name in possibleNamesOfTileMapGameObject)
        {
            if (tileMapGo != null)
            {
                break;
            }

            tileMapGo = scene.Find(name);
        }

        if (tileMapGo == null)
        {
            GameObject[] taggedTileMaps = GameObject.FindGameObjectsWithTag("TileMap");
            foreach (GameObject gameObject in taggedTileMaps)
            {
                if (gameObject.scene.name == scene.name)
                {
                    tileMapGo = gameObject;
                    break;
                }
            }
        }

        int width = -1;
        int height = -1;
        if (tileMapGo == null)
        {
            // return (false, $"Couldn't find '{string.Join("' or '", possibleNamesOfTileMapGameObject)}' GameObject!");
        }
        else
        {
            tk2dTileMap tm = tileMapGo.GetComponent<tk2dTileMap>();
            if (tm == null)
            {
                // return (false, $"Couldn't find 'tk2dTileMap' Component!");
            }
            else
            {
                width = tm.width;
                height = tm.height;
            }
        }

        SvgDocument doc = SvgDocument.Create();
        doc.X = 0;
        doc.Y = 0;
        doc.Width = (width >= 0) ? width : 2; // -1 means no tilemap found, but we still want to paint the scene, so hopefully 2x2 is enough
        doc.Height = (height >= 0) ? height : 2; // -1 means no tilemap found, but we still want to paint the scene, so hopefully 2x2 is enough
        doc.ViewBox = new SvgViewBox()
        {
            Top = 0,
            Left = 0,
            Width = doc.Width,
            Height = doc.Height
        };
        SvgRect backgroundRect = doc.AddRect();
        backgroundRect.Fill = GlobalSettings.EmptyColor;
        backgroundRect.FillOpacity = (width >= 0) ? 1.0 : 0.1;
        backgroundRect.StrokeOpacity = (width >= 0) ? 1.0 : 0.1;
        backgroundRect.X = 0;
        backgroundRect.Y = 0;
        backgroundRect.Width = doc.Width;
        backgroundRect.Height = doc.Height;
        // SvgGroup miscGroup = doc.AddGroup();
        // miscGroup.Fill = GlobalSettings.MiscColor;
        // miscGroup.FillOpacity = 0.25;
        // miscGroup.StrokeOpacity = 0.25;
        SvgGroup enemyGroup = doc.AddGroup();
        enemyGroup.Fill = GlobalSettings.DamageColor;
        enemyGroup.FillOpacity = 1.0;
        enemyGroup.StrokeOpacity = 1.0;
        SvgGroup wallGroup = doc.AddGroup();
        wallGroup.Fill = GlobalSettings.WallColor;
        wallGroup.FillOpacity = 1.0;
        wallGroup.StrokeOpacity = 1.0;

        // List<Collider2D> miscCollider2ds = new List<Collider2D>();
        List<Collider2D> enemyCollider2ds = new List<Collider2D>();
        List<Collider2D> wallCollider2ds = new List<Collider2D>();

        // foreach (var rootGo in scene.GetRootGameObjects())
        // {
        //     foreach (var ec in rootGo.GetComponentsInChildren<EdgeCollider2D>(true))
        //     {
        //         var pgc = ec.gameObject.AddComponent<PolygonCollider2D>();
        //         List<Vector2> points = new List<Vector2>(ec.points);
        //         pgc.pathCount = 1;
        //         pgc.autoTiling = true;
        //         pgc.points = points.ToArray();
        //         pgc.enabled = ec.enabled;
        //         pgc.isTrigger = ec.isTrigger;
        //         pgc.offset = ec.offset;
        //         pgc.sharedMaterial = ec.sharedMaterial;
        //         pgc.usedByComposite = ec.usedByComposite;
        //         pgc.usedByEffector = ec.usedByEffector;
        //         pgc.tag = ec.tag;
        //         pgc.hideFlags = ec.hideFlags;
        //         pgc.name = ec.name;
        //         customPolygonColliderList.Add(pgc);
        //         //UObject.DestroyImmediate(ec);
        //     }
        // }

        foreach (var rootGo in scene.GetRootGameObjects())
        {
            foreach (var collider in rootGo.GetComponentsInChildren<Collider2D>(true))
            {
                if (collider.gameObject.layer is 11 or 17 or 23)
                {
                    enemyCollider2ds.Add(collider);
                }
                else if (collider.gameObject.layer is 0 or 8 && !collider.isTrigger)
                {
                    wallCollider2ds.Add(collider);
                }
                // else
                // {
                //     miscCollider2ds.Add(collider);
                // }
            }
        }

        // SetSVG(miscCollider2ds, miscGroup, height);
        SetSVG(enemyCollider2ds, enemyGroup, height);
        SetSVG(wallCollider2ds, wallGroup, height);

        // foreach (PolygonCollider2D collider2D in customPolygonColliderList)
        // {
        //     UObject.DestroyImmediate(collider2D);
        // }
        using (FileStream outputStream = new FileStream(Path.Combine(_dir, $"{scene.name}.svg"), FileMode.Create))
        {
            doc.Save(outputStream);
        }

        return (true, $"");
    }

    private void SetSVG(List<Collider2D> colliderList, SvgGroup group, float height)
    {
        foreach (var collider2d in colliderList)
        {
            bool colliderIsEnabledAndActive = collider2d.enabled && collider2d.gameObject.activeInHierarchy;
            if (collider2d is PolygonCollider2D polygonCollider2D)
            {
                Transform bcTransform = polygonCollider2D.transform;
                for (int pathId = 0; pathId < polygonCollider2D.pathCount; pathId++)
                {
                    List<double> points = new();
                    foreach (var point in polygonCollider2D.GetPath(pathId))
                    {
                        // The collider's centre point in the world
                        Vector3 worldPosition = bcTransform.TransformPoint(0, 0, 0);

                        // STEP 1: FIND LOCAL, UN-ROTATED CORNERS
                        // Find the 4 corners of the BoxCollider2D in LOCAL space, if the BoxCollider2D had never been rotated
                        Vector3 adjustedPoint = point + polygonCollider2D.offset;
                        adjustedPoint.Scale(bcTransform.lossyScale);

                        // STEP 2: ROTATE CORNERS
                        // Rotate those 4 corners around the centre of the collider to match its transform.rotation
                        adjustedPoint = RotatePointAroundPivot(adjustedPoint, Vector3.zero, bcTransform.eulerAngles);

                        // STEP 3: FIND WORLD POSITION OF CORNERS
                        // Add the 4 rotated corners above to our centre position in WORLD space - and we're done!
                        adjustedPoint = worldPosition + adjustedPoint;
                        adjustedPoint.y = height - adjustedPoint.y; // entire thing upside down

                        points.Add(adjustedPoint.x); // .ToString("G", CultureInfo.InvariantCulture)
                        points.Add(adjustedPoint.y);
                    }

                    SvgPolygon newElement = group.AddPolygon();
                    newElement.FillOpacity = group.FillOpacity * (colliderIsEnabledAndActive ? 1.0 : 0.5);
                    newElement.StrokeOpacity = group.StrokeOpacity * (colliderIsEnabledAndActive ? 1.0 : 0.5);
                    newElement.Points = points.ToArray();
                }
            }
            else if (collider2d is EdgeCollider2D edgeCollider2D)
            {
                Transform bcTransform = edgeCollider2D.transform;

                List<double> points = new();
                foreach (var point in edgeCollider2D.points)
                {
                    // The collider's centre point in the world
                    Vector3 worldPosition = bcTransform.TransformPoint(0, 0, 0);

                    // STEP 1: FIND LOCAL, UN-ROTATED CORNERS
                    // Find the 4 corners of the BoxCollider2D in LOCAL space, if the BoxCollider2D had never been rotated
                    Vector3 adjustedPoint = point + edgeCollider2D.offset;
                    adjustedPoint.Scale(bcTransform.lossyScale);

                    // STEP 2: ROTATE CORNERS
                    // Rotate those 4 corners around the centre of the collider to match its transform.rotation
                    adjustedPoint = RotatePointAroundPivot(adjustedPoint, Vector3.zero, bcTransform.eulerAngles);

                    // STEP 3: FIND WORLD POSITION OF CORNERS
                    // Add the 4 rotated corners above to our centre position in WORLD space - and we're done!
                    adjustedPoint = worldPosition + adjustedPoint;
                    adjustedPoint.y = height - adjustedPoint.y; // entire thing upside down

                    points.Add(adjustedPoint.x); // .ToString("G", CultureInfo.InvariantCulture)
                    points.Add(adjustedPoint.y);
                }

                SvgPolyLine newElement = group.AddPolyLine();
                newElement.FillOpacity = group.FillOpacity * (colliderIsEnabledAndActive ? 1.0 : 0.5);
                newElement.StrokeOpacity = group.StrokeOpacity * (colliderIsEnabledAndActive ? 1.0 : 0.5);
                newElement.Fill = "none";
                newElement.Stroke = group.Fill;
                newElement.StrokeWidth = 0.1;
                newElement.Points = points.ToArray();
            }
            else if (collider2d is CircleCollider2D circleCollider2D)
            {
                Transform bcTransform = circleCollider2D.transform;

                // The collider's centre point in the world
                Vector3 worldPosition = bcTransform.TransformPoint(0, 0, 0);

                // STEP 1: FIND LOCAL, UN-ROTATED CORNERS
                // Find the 4 corners of the BoxCollider2D in LOCAL space, if the BoxCollider2D had never been rotated
                Vector3 adjustedPoint = circleCollider2D.offset;
                adjustedPoint.Scale(bcTransform.lossyScale);

                // STEP 2: ROTATE CORNERS
                // Rotate those 4 corners around the centre of the collider to match its transform.rotation
                adjustedPoint = RotatePointAroundPivot(adjustedPoint, Vector3.zero, bcTransform.eulerAngles);

                // STEP 3: FIND WORLD POSITION OF CORNERS
                // Add the 4 rotated corners above to our centre position in WORLD space - and we're done!
                adjustedPoint = worldPosition + adjustedPoint;
                adjustedPoint.y = height - adjustedPoint.y; // entire thing upside down

                SvgEllipse newElement = group.AddEllipse();
                newElement.FillOpacity = group.FillOpacity * (colliderIsEnabledAndActive ? 1.0 : 0.5);
                newElement.StrokeOpacity = group.StrokeOpacity * (colliderIsEnabledAndActive ? 1.0 : 0.5);
                newElement.CX = adjustedPoint.x;
                newElement.CY = adjustedPoint.y;
                newElement.RX = circleCollider2D.radius * bcTransform.lossyScale.x;
                newElement.RY = circleCollider2D.radius * bcTransform.lossyScale.y;
            }
            else if (collider2d is BoxCollider2D boxCollider2D)
            {
                Transform bcTransform = boxCollider2D.transform;

                // The collider's centre point in the world
                Vector3 worldPosition = bcTransform.TransformPoint(0, 0, 0);

                // STEP 1: FIND LOCAL, UN-ROTATED CORNERS
                // Find the 4 corners of the BoxCollider2D in LOCAL space, if the BoxCollider2D had never been rotated
                Vector3 adjustedPoint = boxCollider2D.offset;
                adjustedPoint.Scale(bcTransform.lossyScale);

                // STEP 2: ROTATE CORNERS
                // Rotate those 4 corners around the centre of the collider to match its transform.rotation
                adjustedPoint = RotatePointAroundPivot(adjustedPoint, Vector3.zero, bcTransform.eulerAngles);

                // STEP 3: FIND WORLD POSITION OF CORNERS
                // Add the 4 rotated corners above to our centre position in WORLD space - and we're done!
                adjustedPoint = worldPosition + adjustedPoint;
                adjustedPoint.y = height - adjustedPoint.y; // entire thing upside down

                SvgRect newElement = group.AddRect();
                newElement.FillOpacity = group.FillOpacity * (colliderIsEnabledAndActive ? 1.0 : 0.5);
                newElement.StrokeOpacity = group.StrokeOpacity * (colliderIsEnabledAndActive ? 1.0 : 0.5);
                newElement.X = adjustedPoint.x - ((boxCollider2D.size.x / 2) * bcTransform.lossyScale.x);
                newElement.Y = adjustedPoint.y - ((boxCollider2D.size.y / 2) * bcTransform.lossyScale.y);
                newElement.Width = boxCollider2D.size.x * bcTransform.lossyScale.x;
                newElement.Height = boxCollider2D.size.y * bcTransform.lossyScale.y;
                // negative scale adjustment
                if (newElement.Width < 0)
                {
                    newElement.X -= Math.Abs(newElement.Width);
                    newElement.Width *= -1.0;
                }

                if (newElement.Height < 0)
                {
                    newElement.Y -= Math.Abs(newElement.Height);
                    newElement.Height *= -1.0;
                }
            }
        }
    }

    // Helper method courtesy of @aldonaletto
    // http://answers.unity3d.com/questions/532297/rotate-a-vector-around-a-certain-point.html
    private Vector3 RotatePointAroundPivot(Vector3 point, Vector3 pivot, Vector3 angles)
    {
        Vector3 dir = point - pivot; // get point direction relative to pivot
        dir = Quaternion.Euler(angles) * dir; // rotate it
        point = dir + pivot; // calculate rotated point
        return point; // return it
    }
}

public static class SvgExtension
{
    public static XmlElement GetElement(this SvgElement elem)
    {
        return ReflectionHelper.GetField<SvgElement, XmlElement>(elem, "Element");
    }
}