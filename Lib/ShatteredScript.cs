using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;
using RT.Util.Geometry;
using RT.KitchenSink.Geometry;
using RT.Util.ExtensionMethods;
using System.Collections;

public class ShatteredScript : MonoBehaviour {

    public KMAudio audio;
    public KMSelectable modSel;
    public GameObject shardTemplate;
    public GameObject statusLight;
    public GameObject fullMirror;
    public GameObject solveText;
    public Light solveLight;
    public Material[] debugMats;

    VoronoiDiagram voronoi;
    PointD[] shardLabelPositions;
    (CollisionDetect cd, KMSelectable sel, MeshRenderer rend)[] shards;
    int heldShard;
    const int totalShards = 10;
    bool[] hasBeenPlaced;
    bool focused;

    bool debugMode = true; // Debug shard status (green = in frame and not colliding, red = outside frame or colliding)

    static int moduleIdCounter = 1;
    int moduleId;
    private bool moduleSolved;

    void Awake()
    {
        // Update moduleId field for logging purposes
        moduleId = moduleIdCounter++;
    }

    void Start()
    {
        // Generate randomized shattered mirror
        // Achieved with modified Voronoi diagram code from Voronoi Maze by Timwi
        var rnd = new System.Random(UnityEngine.Random.Range(0, int.MaxValue));

        tryEverythingAgain:
        var pointsList = new List<PointD>();
        while (pointsList.Count < totalShards)
        {
            var newPoint = new PointD(rnd.NextDouble(), rnd.NextDouble());
            if (pointsList.Any(p => p.Distance(newPoint) < 1 / 128d))
                continue;
            pointsList.Add(newPoint);
        }
        voronoi = VoronoiDiagram.GenerateVoronoiDiagram(pointsList.ToArray(), 1, 1, VoronoiDiagramFlags.IncludeEdgePolygons);
        shardLabelPositions = voronoi.Polygons.Select(p => p.GetLabelPoint(.005)).ToArray();

        // Discard unwanted Voronoi diagrams
        if (voronoi.Polygons.Any(poly => poly.Vertices.ConsecutivePairs(true).Any(pair => pair.Item1.Distance(pair.Item2) < .05)) ||
            voronoi.Edges.Any(e => Math.Min(e.edge.Start.Distance(), e.edge.End.Distance()) < .05) ||
            voronoi.Edges.Any(e => e.edge.Distance(shardLabelPositions[e.siteA]) < .025 || e.edge.Distance(shardLabelPositions[e.siteB]) < .025))
            goto tryEverythingAgain;

        var points = pointsList.ToArray();
        var numShards = points.Length;

        shards = new (CollisionDetect cd, KMSelectable sel, MeshRenderer rend)[numShards];
        var children = new List<KMSelectable>();
        for (var shardIx = 0; shardIx < points.Length; shardIx++)
        {
            var polygon = voronoi.Polygons[shardIx];
            var obj = Instantiate(shardTemplate, transform);
            var selectable = obj.GetComponentInChildren<KMSelectable>();
            children.Add(selectable);
            var meshFilter = selectable.GetComponent<MeshFilter>();

            // Shard and collider
            var shardMeshTris = new List<int>();
            var colliderMeshTris = new List<Vector3>();
            var edgeLengths = new List<double>();
            for (var i = 0; i < polygon.Vertices.Count; i++)
            {
                var j = (i + 1) % polygon.Vertices.Count;

                const float colliderTop = 0;
                const float colliderBottom = -.03f;
                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[i], colliderBottom));
                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[i], colliderTop));
                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[j], colliderTop));

                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[j], colliderTop));
                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[j], colliderBottom));
                colliderMeshTris.Add(convertPointToVector(polygon.Vertices[i], colliderBottom));

                if (i != 0 && j != 0)
                {
                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[0], colliderTop));
                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[j], colliderTop));
                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[i], colliderTop));

                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[0], colliderBottom));
                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[i], colliderBottom));
                    colliderMeshTris.Add(convertPointToVector(polygon.Vertices[j], colliderBottom));

                    shardMeshTris.Add(0);
                    shardMeshTris.Add(j);
                    shardMeshTris.Add(i);
                }

                edgeLengths.Add(polygon.Vertices[j].Distance(polygon.Vertices[i]));
            }
            var colliderMesh = new Mesh();
            colliderMesh.vertices = colliderMeshTris.ToArray();
            colliderMesh.triangles = Enumerable.Range(0, colliderMeshTris.Count).ToArray();
            colliderMesh.normals = Enumerable.Repeat(new Vector3(0, 1, 0), colliderMeshTris.Count).ToArray();
            selectable.gameObject.GetComponent<MeshCollider>().sharedMesh = colliderMesh;
            selectable.transform.GetChild(1).GetComponent<MeshCollider>().sharedMesh = colliderMesh;

            var shardMesh = new Mesh();
            shardMesh.vertices = shardMeshTris.Select(pIx => convertPointToVector(polygon.Vertices[pIx], 0)).ToArray();
            shardMesh.triangles = Enumerable.Range(0, shardMeshTris.Count).ToArray();
            shardMesh.normals = Enumerable.Repeat(new Vector3(0, 1, 0), shardMeshTris.Count).ToArray();
            var totalLength = edgeLengths.Sum();
            Vector2 uvVector(double proportion) => new Vector2(Mathf.Cos(2 * Mathf.PI * (float)proportion), Mathf.Sin(2 * Mathf.PI * (float)proportion)) * .5f + new Vector2(.5f, .5f);
            shardMesh.uv = shardMeshTris.Select(ix => uvVector(edgeLengths.Take(ix).Sum() / totalLength)).ToArray();
            meshFilter.sharedMesh = shardMesh;

            // Shrink shards a little so they fit better
            selectable.transform.localScale = new Vector3(0.98f, 1, 0.98f);

            // Fix shard mesh center point being off center
            selectable.transform.localPosition = convertPointToVector(polygon.Centroid(), 0) * -1;

            // Rotate shard by random 90 degree angle
            selectable.transform.parent.transform.localEulerAngles += new Vector3(0, 90 * UnityEngine.Random.Range(0, 4), 0);

            shards[shardIx] = (selectable.GetComponentInChildren<CollisionDetect>(), selectable, selectable.GetComponent<MeshRenderer>());
            int sIx = shardIx;
            selectable.OnInteract = delegate () { ShardPressed(sIx); return false; };
            selectable.OnInteractEnded = delegate () { ShardReleased(sIx); };
        }

        Destroy(shardTemplate.gameObject);

        modSel.Children = children.ToArray();
        modSel.UpdateChildren();

        hasBeenPlaced = new bool[shards.Length];
        for (int i = 0; i < shards.Length; i++)
            shards[i].sel.gameObject.SetActive(false);

        modSel.OnFocus += delegate () { focused = true; };
        modSel.OnDefocus += delegate () { focused = false; };
        if (Application.isEditor) focused = true;

        solveLight.range *= transform.lossyScale.x;

        statusLight.SetActive(false);
    }

    const double cf = .0835 / .5;
    PointD convertPoint(PointD orig) => (orig - new PointD(.5, .5)) * cf;
    Vector3 convertPointToVector(PointD orig, float y)
    {
        var p = convertPoint(orig);
        return new Vector3((float)p.X, y, (float)p.Y);
    }

    void Update()
    {
        if (heldShard != -1)
        {
            shards[heldShard].sel.gameObject.SetActive(focused);
            if (shards[heldShard].sel.gameObject.activeSelf)
            {
                float distanceToScreen = Camera.main.WorldToScreenPoint(shards[heldShard].sel.transform.parent.transform.position).z;
                Vector3 posMove = Camera.main.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, distanceToScreen));
                shards[heldShard].sel.transform.parent.transform.position = new Vector3(posMove.x, shards[heldShard].sel.transform.parent.transform.position.y, posMove.z);
                shards[heldShard].sel.transform.parent.transform.localPosition = new Vector3(shards[heldShard].sel.transform.parent.transform.localPosition.x, 0.03f, shards[heldShard].sel.transform.parent.transform.localPosition.z);
                if (Input.GetKeyDown(KeyCode.R))
                    shards[heldShard].sel.transform.parent.transform.localEulerAngles += new Vector3(0, 90, 0);
            }
        }
        if (debugMode)
        {
            for (int i = 0; i < shards.Length; i++)
            {
                if (!shards[i].cd.isColliding && !ShardOutsideBounds(shards[i].sel.transform.parent.transform))
                    shards[i].rend.material = debugMats[0];
                else
                    shards[i].rend.material = debugMats[1];
            }
        }
    }

    void ShardPressed(int shardIndex)
    {
        if (moduleSolved) return;
        if (hasBeenPlaced.All(x => x))
        {
            audio.PlaySoundAtTransform("shardUp", shards[shardIndex].sel.transform);
            heldShard = shardIndex;
        }
        else
        {
            audio.PlaySoundAtTransform("shardDown", shards[shardIndex].sel.transform);
            if (!ShardOutsideBounds(shards[shardIndex].sel.transform.parent.transform))
                shards[shardIndex].sel.transform.parent.transform.localPosition = new Vector3(shards[shardIndex].sel.transform.parent.transform.localPosition.x, 0.0101f, shards[shardIndex].sel.transform.parent.transform.localPosition.z);
            hasBeenPlaced[shardIndex] = true;
            for (int i = 0; i < hasBeenPlaced.Length; i++)
            {
                if (!hasBeenPlaced[i])
                {
                    heldShard = i;
                    shards[i].sel.gameObject.SetActive(true);
                    break;
                }
            }
            if (hasBeenPlaced.All(x => x))
                heldShard = -1;
        }
    }

    void ShardReleased(int shardIndex)
    {
        if (moduleSolved) return;
        if (hasBeenPlaced.All(x => x) && heldShard != -1)
        {
            audio.PlaySoundAtTransform("shardDown", shards[shardIndex].sel.transform);
            heldShard = -1;
            if (!ShardOutsideBounds(shards[shardIndex].sel.transform.parent.transform))
                shards[shardIndex].sel.transform.parent.transform.localPosition = new Vector3(shards[shardIndex].sel.transform.parent.transform.localPosition.x, 0.0101f, shards[shardIndex].sel.transform.parent.transform.localPosition.z);
            for (int i = 0; i < shards.Length; i++)
                if (shards[i].cd.isColliding || ShardOutsideBounds(shards[i].sel.transform.parent.transform))
                    return;
            moduleSolved = true;
            Debug.LogFormat("[Shattered #{0}] Module solved", moduleId);
            audio.PlaySoundAtTransform("solveMirror", transform);
            StartCoroutine(SolveAnimation());
        }
    }

    bool ShardOutsideBounds(Transform trans)
    {
        if (trans.localPosition.x < -.1f || trans.localPosition.x > .1f || trans.localPosition.z < -.1f || trans.localPosition.z > .1f)
            return true;
        else
            return false;
    }

    IEnumerator SolveAnimation()
    {
        float t = 0;
        while (t < 5f)
        {
            t += Time.deltaTime;
            solveLight.intensity = t * 2;
            yield return null;
        }
        for (int i = 1; i < shards.Length; i++)
            shards[i].sel.gameObject.SetActive(false);
        fullMirror.SetActive(true);
        solveText.SetActive(true);
        t = 0;
        while (t < 5f)
        {
            t += Time.deltaTime * 3f;
            solveLight.intensity = 10 - (t * 2);
            yield return null;
        }
        GetComponent<KMBombModule>().HandlePass();
    }
}