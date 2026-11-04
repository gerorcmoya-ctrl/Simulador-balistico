using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class BallisticSimulator : MonoBehaviour
{
    // ------------------------- Parámetros ----------------------------
    const float BarrelLength = 3f;                       // m: largo del cañón (la fuerza actúa en este tramo)
    static readonly Vector3 CannonPos = new Vector3(-12f, 1.2f, 0f);
    const float PostImpactWait = 4f;                     // s que se espera tras el 1er impacto
    const float MaxAttemptTime = 15f;                    // s máximos por intento

    // ------------------------- Estado --------------------------------
    Transform envRoot, cannonRoot, targetsRoot, projectilesRoot;
    Transform pivot;
    LineRenderer trajectory, springLine;
    Transform springBlock;
    Vector3 springAnchor;
    Font font;

    Slider angleSlider, forceSlider, massSlider;
    Text angleLabel, forceLabel, massLabel, reportText, scoreText;
    Button fireButton;

    float angle = 45f, force = 80f, mass = 2f;
    int totalScore, attempts;
    bool busy;
    GameObject currentProjectile;
    Rigidbody projectileRb;
    TargetPiece[] pieces = new TargetPiece[0];
    Vector3 dianaCenter;

    // Datos del tiro actual
    class ShotData
    {
        public float launchTime, v0, energy, maxHeight;
        public Vector3 launchPos;
        public int impacts;
        public float totalImpulse, maxRelVel;
        public bool hasAnyImpact; public float firstImpactTime, flightTime; public Vector3 firstImpactPoint; public string firstImpactName; public float firstRelVel, firstImpulse;
        public bool hasTargetImpact; public float targetTime; public Vector3 targetPoint; public string targetName; public float targetRelVel, targetImpulse;
    }
    ShotData shot;
    bool shotActive;

    // ------------------------- Bootstrap -----------------------------
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
#if UNITY_2023_1_OR_NEWER
        if (FindFirstObjectByType<BallisticSimulator>() != null) return;
#else
        if (FindObjectOfType<BallisticSimulator>() != null) return;
#endif
        new GameObject("BallisticSimulator").AddComponent<BallisticSimulator>();
    }

    void Start()
    {
        font = LoadFont();
        envRoot = NewRoot("Entorno");
        cannonRoot = NewRoot("Canon");
        targetsRoot = NewRoot("Objetivos");
        projectilesRoot = NewRoot("Proyectiles");

        SetupCameraAndLight();
        BuildEnvironment();
        BuildCannon();
        BuildTargets();
        BuildUI();
        UpdateLabels();
        UpdateCannonAndPreview();
    }

    Transform NewRoot(string n)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    static Font LoadFont()
    {
        Font f = null;
#if UNITY_2022_2_OR_NEWER
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
#else
        try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
#endif
        if (f == null) f = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Helvetica", "Liberation Sans", "DejaVu Sans" }, 18);
        return f;
    }

    // ------------------------- Cámara y luz --------------------------
    void SetupCameraAndLight()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var g = new GameObject("Main Camera");
            g.tag = "MainCamera";
            cam = g.AddComponent<Camera>();
            g.AddComponent<AudioListener>();
        }
        cam.transform.position = new Vector3(5f, 6f, -38f);
        cam.transform.rotation = Quaternion.LookRotation(new Vector3(5f, 5f, 0f) - cam.transform.position);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
        cam.fieldOfView = 50f;

#if UNITY_2023_1_OR_NEWER
        bool hasLight = FindFirstObjectByType<Light>() != null;
#else
        bool hasLight = FindObjectOfType<Light>() != null;
#endif
        if (!hasLight)
        {
            var lg = new GameObject("Luz direccional");
            lg.transform.SetParent(envRoot, false);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
            lg.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }
    }

    // ------------------------- Entorno -------------------------------
    void BuildEnvironment()
    {
        var ground = Prim("Suelo", PrimitiveType.Cube, new Vector3(0, -0.5f, 0), new Vector3(400, 1, 20), new Color(0.35f, 0.6f, 0.3f), envRoot);
        ground.GetComponent<Collider>().material = null;

        // Marcas de distancia cada 5 m
        for (int x = -10; x <= 40; x += 5)
        {
            var m = Prim("Marca " + x, PrimitiveType.Cube, new Vector3(x, 0.01f, 0), new Vector3(0.12f, 0.02f, 20f), Color.white, envRoot);
            Destroy(m.GetComponent<Collider>());
        }
    }

    void BuildCannon()
    {
        var baseGo = Prim("Base canon", PrimitiveType.Cylinder, new Vector3(CannonPos.x, 0.6f, 0), new Vector3(1.4f, 0.6f, 1.4f), new Color(0.3f, 0.3f, 0.35f), cannonRoot);
        Destroy(baseGo.GetComponent<Collider>());

        var pv = new GameObject("Pivote canon");
        pv.transform.SetParent(cannonRoot, false);
        pv.transform.position = CannonPos;
        pivot = pv.transform;

        var barrel = Prim("Canon", PrimitiveType.Cube, Vector3.zero, new Vector3(BarrelLength, 0.45f, 0.45f), new Color(0.15f, 0.15f, 0.18f), pivot);
        barrel.transform.localPosition = new Vector3(BarrelLength * 0.5f, 0, 0);
        Destroy(barrel.GetComponent<Collider>());

        trajectory = MakeLine("Trayectoria prevista", new Color(1f, 0.9f, 0.1f, 0.9f), 0.08f, cannonRoot);
    }

    // ------------------------- Objetivos -----------------------------
    GameObject Body(string name, PrimitiveType type, Vector3 pos, Vector3 scale, Color c, float m, int points, float move = 0.7f, float tilt = 35f)
    {
        var go = Prim(name, type, pos, scale, c, targetsRoot);
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = m;
        rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
        rb.solverIterations = 20;
        rb.solverVelocityIterations = 10;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        var p = go.AddComponent<TargetPiece>();
        p.points = points; p.moveThreshold = move; p.tiltThreshold = tilt;
        return go;
    }

    static FixedJoint Fix(GameObject a, GameObject b, float breakForce)
    {
        var j = a.AddComponent<FixedJoint>();
        j.connectedBody = b.GetComponent<Rigidbody>();
        j.breakForce = breakForce;
        j.breakTorque = breakForce;
        return j;
    }

    void BuildTargets()
    {
        // Reconstrucción limpia
        if (targetsRoot != null && targetsRoot.childCount > 0)
        {
            targetsRoot.gameObject.SetActive(false);
            Destroy(targetsRoot.gameObject);
            targetsRoot = NewRoot("Objetivos");
        }

        // 1) Pórtico: 2 columnas + viga + diana (FixedJoint)
        GameObject[] topBoxes = new GameObject[2];
        float[] colX = { 7f, 10f };
        for (int c = 0; c < 2; c++)
        {
            GameObject prev = null;
            for (int i = 0; i < 3; i++)
            {
                var b = Body("Columna " + (c + 1) + "." + (i + 1), PrimitiveType.Cube, new Vector3(colX[c], 0.5f + i, 0), Vector3.one, new Color(0.85f, 0.65f, 0.35f), 1f, 40);
                if (prev != null) Fix(b, prev, 450f);
                prev = b;
            }
            topBoxes[c] = prev;
        }
        var beam = Body("Viga", PrimitiveType.Cube, new Vector3(8.5f, 3.2f, 0), new Vector3(4.2f, 0.4f, 1f), new Color(0.55f, 0.35f, 0.2f), 2f, 100);
        Fix(beam, topBoxes[0], 450f);
        Fix(beam, topBoxes[1], 450f);
        var diana = Body("Diana", PrimitiveType.Cube, new Vector3(8.5f, 3.8f, 0), new Vector3(0.8f, 0.8f, 1f), new Color(0.9f, 0.1f, 0.1f), 1f, 300);
        dianaCenter = diana.transform.position;

        // 2) Torre de cajas (FixedJoint)
        GameObject prevBox = null;
        for (int i = 0; i < 4; i++)
        {
            var b = Body("Torre " + (i + 1), PrimitiveType.Cube, new Vector3(13f, 0.5f + i, 0), Vector3.one, Color.Lerp(new Color(0.9f, 0.75f, 0.2f), new Color(0.9f, 0.4f, 0.1f), i / 3f), 1f, 50);
            if (prevBox != null) Fix(b, prevBox, 300f);
            prevBox = b;
        }

        // 3) Péndulo colgante (HingeJoint) + caja unida con FixedJoint
        var post = Prim("Soporte pendulo", PrimitiveType.Cube, new Vector3(18.2f, 2.1f, 0), new Vector3(0.25f, 4.2f, 0.25f), new Color(0.3f, 0.3f, 0.3f), targetsRoot);
        var arm = Prim("Brazo pendulo", PrimitiveType.Cube, new Vector3(17.6f, 4.2f, 0), new Vector3(1.4f, 0.2f, 0.25f), new Color(0.3f, 0.3f, 0.3f), targetsRoot);
        var plank = Body("Pendulo", PrimitiveType.Cube, new Vector3(17f, 2.5f, 0), new Vector3(0.4f, 3f, 1f), new Color(0.4f, 0.5f, 0.8f), 2f, 150, 0.7f, 40f);
        var hinge = plank.AddComponent<HingeJoint>();
        hinge.anchor = new Vector3(0, 0.5f, 0);       // extremo superior de la tabla
        hinge.axis = new Vector3(0, 0, 1);
        hinge.autoConfigureConnectedAnchor = true;     // ancla fija al mundo
        var hang = Body("Caja colgante", PrimitiveType.Cube, new Vector3(17f, 0.5f, 0), Vector3.one, new Color(0.3f, 0.4f, 0.9f), 3f, 100);
        Fix(hang, plank, 350f);

        // 4) Bloque suspendido de un resorte (SpringJoint)
        var sPost = Prim("Soporte resorte", PrimitiveType.Cube, new Vector3(22.4f, 3.3f, 0), new Vector3(0.25f, 6.6f, 0.25f), new Color(0.3f, 0.3f, 0.3f), targetsRoot);
        var sArm = Prim("Brazo resorte", PrimitiveType.Cube, new Vector3(21f, 6.6f, 0), new Vector3(3f, 0.2f, 0.25f), new Color(0.3f, 0.3f, 0.3f), targetsRoot);
        float k = 200f, restLen = 1.5f, blockMass = 2f;
        float stretch = blockMass * 9.81f / k;                       // equilibrio estático
        springAnchor = new Vector3(21f, 6.5f, 0);
        float topY = springAnchor.y - restLen - stretch;
        var block = Body("Bloque resorte", PrimitiveType.Cube, new Vector3(21f, topY - 0.5f, 0), Vector3.one, new Color(0.2f, 0.8f, 0.7f), blockMass, 250, 1.5f, 90f);
        var sj = block.AddComponent<SpringJoint>();
        sj.autoConfigureConnectedAnchor = false;
        sj.anchor = new Vector3(0, 0.5f, 0);
        sj.connectedAnchor = springAnchor;
        sj.spring = k;
        sj.damper = 10f;
        sj.minDistance = restLen;
        sj.maxDistance = restLen;
        springBlock = block.transform;
        springLine = MakeLine("Linea resorte", new Color(0.9f, 0.9f, 0.9f), 0.05f, targetsRoot);

        pieces = targetsRoot.GetComponentsInChildren<TargetPiece>();
    }

    // ------------------------- Disparo -------------------------------
    float LaunchSpeed() { return Mathf.Sqrt(2f * force * BarrelLength / mass); } // trabajo F*L = 1/2 m v²

    void Fire()
    {
        if (busy) return;
        busy = true;
        fireButton.interactable = false;
        trajectory.enabled = false;

        if (currentProjectile != null) Destroy(currentProjectile);
        foreach (var p in pieces) if (p != null) p.Arm();

        Vector3 dir = pivot.right;
        Vector3 muzzle = pivot.position + dir * (BarrelLength + 0.2f);

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Proyectil";
        go.transform.SetParent(projectilesRoot, false);
        float d = 0.3f * Mathf.Pow(mass, 1f / 3f) + 0.15f;
        go.transform.position = muzzle;
        go.transform.localScale = Vector3.one * d;
        go.GetComponent<Renderer>().material.color = new Color(0.1f, 0.1f, 0.1f);

        var rb = go.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.solverIterations = 20;

        var sensor = go.AddComponent<ProjectileSensor>();
        sensor.owner = this;

        var tr = go.AddComponent<TrailRenderer>();
        tr.time = 2f; tr.startWidth = 0.12f; tr.endWidth = 0f;
        tr.material = new Material(Shader.Find("Sprites/Default"));
        tr.startColor = new Color(1f, 0.8f, 0.1f, 1f);
        tr.endColor = new Color(1f, 0.8f, 0.1f, 0f);

        float v0 = LaunchSpeed();
        rb.AddForce(dir * mass * v0, ForceMode.Impulse);     // impulso = m * v0

        currentProjectile = go;
        projectileRb = rb;
        attempts++;
        shot = new ShotData { launchTime = Time.time, v0 = v0, energy = force * BarrelLength, launchPos = muzzle, maxHeight = muzzle.y };
        shotActive = true;
        reportText.text = "<b>Disparando...</b>";
        StartCoroutine(AttemptRoutine());
    }

    void FixedUpdate()
    {
        if (shotActive && currentProjectile != null)
            shot.maxHeight = Mathf.Max(shot.maxHeight, currentProjectile.transform.position.y);
    }

    public void RegisterImpact(Collision c)
    {
        if (!shotActive || shot == null) return;
        var piece = c.collider.GetComponentInParent<TargetPiece>();
        float imp = c.impulse.magnitude;
        float rv = c.relativeVelocity.magnitude;
        Vector3 pt = c.contactCount > 0 ? c.GetContact(0).point : c.transform.position;

        shot.impacts++;
        shot.totalImpulse += imp;
        shot.maxRelVel = Mathf.Max(shot.maxRelVel, rv);

        if (!shot.hasAnyImpact)
        {
            shot.hasAnyImpact = true;
            shot.firstImpactTime = Time.time;
            shot.flightTime = Time.time - shot.launchTime;
            shot.firstImpactPoint = pt;
            shot.firstImpactName = piece != null ? piece.name : "Suelo";
            shot.firstRelVel = rv;
            shot.firstImpulse = imp;
        }
        if (piece != null && !shot.hasTargetImpact)
        {
            shot.hasTargetImpact = true;
            shot.targetTime = Time.time - shot.launchTime;
            shot.targetPoint = pt;
            shot.targetName = piece.name;
            shot.targetRelVel = rv;
            shot.targetImpulse = imp;
        }
    }

    IEnumerator AttemptRoutine()
    {
        float start = Time.time;
        var wait = new WaitForSeconds(0.1f);
        while (true)
        {
            yield return wait;
            if (currentProjectile == null) break;
            if (currentProjectile.transform.position.y < -10f) break;
            if (shot.hasAnyImpact && Time.time - shot.firstImpactTime > PostImpactWait) break;
            if (Time.time - start > MaxAttemptTime) break;
        }
        FinishAttempt();
    }

    void FinishAttempt()
    {
        shotActive = false;

        int knocked = 0, piecePoints = 0, broken = 0;
        foreach (var p in pieces)
        {
            if (p == null) continue;
            broken += p.BrokenJoints;
            if (p.Knocked) { knocked++; piecePoints += p.points; }
        }

        float dist = -1f; int precisionBonus = 0;
        if (shot.hasTargetImpact)
        {
            dist = Vector2.Distance(shot.targetPoint, dianaCenter);
            precisionBonus = Mathf.RoundToInt(200f * Mathf.Max(0f, 1f - dist / 10f));
        }
        int score = piecePoints + precisionBonus;
        totalScore += score;

        // Datos principales: impacto contra objetivo si hubo, si no el primer impacto
        Vector3 ip = shot.hasTargetImpact ? shot.targetPoint : shot.firstImpactPoint;
        float rel = shot.hasTargetImpact ? shot.targetRelVel : shot.firstRelVel;
        float impulse = shot.hasTargetImpact ? shot.targetImpulse : shot.firstImpulse;
        string against = shot.hasTargetImpact ? shot.targetName : (shot.hasAnyImpact ? shot.firstImpactName : "Nada (fuera de mapa)");

        var sb = new StringBuilder();
        sb.AppendLine("<b>REPORTE DE TIRO #" + attempts + "</b>");
        sb.AppendLine("Ángulo " + angle.ToString("F0") + "°  |  Fuerza " + force.ToString("F0") + " N  |  Masa " + mass.ToString("F1") + " kg");
        sb.AppendLine("Velocidad inicial: " + shot.v0.ToString("F1") + " m/s");
        sb.AppendLine("Energía inicial (potencia): " + shot.energy.ToString("F0") + " J");
        sb.AppendLine("Altura máxima: " + shot.maxHeight.ToString("F1") + " m");
        sb.AppendLine("");
        sb.AppendLine("<b>Impacto</b>");
        sb.AppendLine("Contra: " + against);
        if (shot.hasAnyImpact)
        {
            sb.AppendLine("Tiempo de vuelo (1er impacto): " + shot.flightTime.ToString("F2") + " s");
            sb.AppendLine("Punto de impacto: (" + ip.x.ToString("F1") + ", " + ip.y.ToString("F1") + ") m");
            sb.AppendLine("Alcance horizontal: " + (ip.x - shot.launchPos.x).ToString("F1") + " m");
            sb.AppendLine("Velocidad relativa: " + rel.ToString("F1") + " m/s");
            sb.AppendLine("Impulso de colisión: " + impulse.ToString("F1") + " N·s");
            sb.AppendLine("Impulso total acumulado: " + shot.totalImpulse.ToString("F1") + " N·s (" + shot.impacts + " contactos)");
        }
        sb.AppendLine("");
        sb.AppendLine("<b>Resultado</b>");
        sb.AppendLine("Piezas derribadas: " + knocked + " / " + pieces.Length);
        sb.AppendLine("Uniones rotas: " + broken);
        if (dist >= 0f) sb.AppendLine("Distancia a la diana: " + dist.ToString("F1") + " m");
        else sb.AppendLine("Distancia a la diana: sin impacto en objetivos");
        sb.AppendLine("Puntos piezas: " + piecePoints + "  +  precisión: " + precisionBonus);
        sb.AppendLine("<b>PUNTUACIÓN DEL TIRO: " + score + "</b>");
        reportText.text = sb.ToString();
        scoreText.text = "Puntaje total: " + totalScore + "   (último tiro: " + score + ")";

        busy = false;
        fireButton.interactable = true;
        trajectory.enabled = true;
        UpdateCannonAndPreview();
    }

    void ResetTargets()
    {
        if (busy) return;
        if (currentProjectile != null) Destroy(currentProjectile);
        BuildTargets();
        reportText.text = "Objetivos reiniciados.\nAjustá los controles y presioná DISPARAR.";
    }

    // ------------------------- Previsualización ----------------------
    void UpdateCannonAndPreview()
    {
        if (pivot == null) return;
        pivot.rotation = Quaternion.Euler(0, 0, angle);
        Vector3 dir = pivot.right;
        Vector3 start = pivot.position + dir * (BarrelLength + 0.2f);
        float v0 = LaunchSpeed();
        Vector3 v = dir * v0;
        const int N = 120; const float dt = 0.05f;
        var pts = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i < N; i++)
        {
            float t = i * dt;
            Vector3 p = start + v * t + 0.5f * Physics.gravity * t * t;
            p.z = 0;
            pts.Add(p);
            if (p.y < 0f) break;
        }
        trajectory.positionCount = pts.Count;
        trajectory.SetPositions(pts.ToArray());
    }

    void Update()
    {
        if (springLine != null && springBlock != null)
        {
            springLine.positionCount = 2;
            springLine.SetPosition(0, springAnchor);
            springLine.SetPosition(1, springBlock.TransformPoint(new Vector3(0, 0.5f, 0)));
        }
    }

    // ------------------------- UI ------------------------------------
    void BuildUI()
    {
        // EventSystem (compatible con Input Manager viejo y nuevo)
#if UNITY_2023_1_OR_NEWER
        bool hasES = FindFirstObjectByType<EventSystem>() != null;
#else
        bool hasES = FindObjectOfType<EventSystem>() != null;
#endif
        if (!hasES)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var t = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (t != null) es.AddComponent(t); else es.AddComponent<StandaloneInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        var cgo = new GameObject("UI");
        cgo.transform.SetParent(transform, false);
        var canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        cgo.AddComponent<GraphicRaycaster>();
        var root = cgo.transform;

        // Panel de controles (arriba izquierda)
        var panel = NewRect("Panel controles", root);
        Anchor(panel, new Vector2(0, 1), new Vector2(20, -20), new Vector2(430, 340));
        panel.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.6f);
        var pt = panel.transform;

        MakeText(pt, "Titulo", "<b>SIMULADOR BALÍSTICO</b>", 24, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(15, -8), new Vector2(400, 32));

        angleLabel = MakeText(pt, "LblAngulo", "", 20, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(15, -48), new Vector2(400, 28));
        angleSlider = MakeSlider(pt, "SliderAngulo", 5, 85, angle, new Vector2(15, -76), v => { angle = v; UpdateLabels(); UpdateCannonAndPreview(); });

        forceLabel = MakeText(pt, "LblFuerza", "", 20, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(15, -108), new Vector2(400, 28));
        forceSlider = MakeSlider(pt, "SliderFuerza", 10, 300, force, new Vector2(15, -136), v => { force = v; UpdateLabels(); UpdateCannonAndPreview(); });

        massLabel = MakeText(pt, "LblMasa", "", 20, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(15, -168), new Vector2(400, 28));
        massSlider = MakeSlider(pt, "SliderMasa", 1, 10, mass, new Vector2(15, -196), v => { mass = v; UpdateLabels(); UpdateCannonAndPreview(); });

        fireButton = MakeButton(pt, "DISPARAR", new Vector2(15, -240), new Vector2(195, 52), new Color(0.8f, 0.2f, 0.15f), Fire);
        MakeButton(pt, "REINICIAR", new Vector2(220, -240), new Vector2(195, 52), new Color(0.2f, 0.4f, 0.75f), ResetTargets);

        MakeText(pt, "Ayuda", "La fuerza actúa a lo largo del cañón (3 m):\nv0 = √(2·F·L / m). Línea amarilla = trayectoria prevista.", 16, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(15, -302), new Vector2(400, 40));

        // Puntaje (arriba centro)
        scoreText = MakeText(root, "Puntaje", "Puntaje total: 0", 34, TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -15), new Vector2(900, 50));
        scoreText.color = new Color(1f, 1f, 0.6f);
        var outline = scoreText.gameObject.AddComponent<Outline>();
        outline.effectColor = Color.black;

        // Reporte (arriba derecha)
        var rpanel = NewRect("Panel reporte", root);
        Anchor(rpanel, new Vector2(1, 1), new Vector2(-20, -20), new Vector2(560, 560));
        rpanel.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.6f);
        reportText = MakeText(rpanel, "Reporte", "Ajustá los controles y presioná DISPARAR.", 20, TextAnchor.UpperLeft, new Vector2(0, 1), Vector2.zero, Vector2.zero);
        Stretch(reportText.rectTransform);
        reportText.rectTransform.offsetMin = new Vector2(15, 10);
        reportText.rectTransform.offsetMax = new Vector2(-15, -10);
    }

    void UpdateLabels()
    {
        if (angleLabel == null) return;
        angleLabel.text = "Ángulo: " + angle.ToString("F0") + "°";
        forceLabel.text = "Fuerza: " + force.ToString("F0") + " N   (v0 = " + LaunchSpeed().ToString("F1") + " m/s)";
        massLabel.text = "Masa del proyectil: " + mass.ToString("F0") + " kg";
    }

    // ------------------------- Helpers UI ----------------------------
    static RectTransform NewRect(string n, Transform parent)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    Text MakeText(Transform parent, string name, string content, int size, TextAnchor align, Vector2 anchor, Vector2 pos, Vector2 sz)
    {
        var rt = NewRect(name, parent);
        Anchor(rt, anchor, pos, sz);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = font; t.text = content; t.fontSize = size; t.alignment = align; t.color = Color.white;
        t.supportRichText = true;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    Slider MakeSlider(Transform parent, string name, float min, float max, float val, Vector2 pos, UnityAction<float> cb)
    {
        var rt = NewRect(name, parent);
        Anchor(rt, new Vector2(0, 1), pos, new Vector2(400, 24));

        var bg = NewRect("Background", rt); Stretch(bg);
        var bgImg = bg.gameObject.AddComponent<Image>(); bgImg.color = new Color(0.2f, 0.2f, 0.2f, 1f);

        var fa = NewRect("Fill Area", rt);
        fa.anchorMin = new Vector2(0, 0.3f); fa.anchorMax = new Vector2(1, 0.7f);
        fa.offsetMin = new Vector2(8, 0); fa.offsetMax = new Vector2(-8, 0);
        var fill = NewRect("Fill", fa);
        fill.anchorMin = new Vector2(0, 0); fill.anchorMax = new Vector2(0, 1);
        fill.sizeDelta = new Vector2(10, 0);
        var fillImg = fill.gameObject.AddComponent<Image>(); fillImg.color = new Color(1f, 0.6f, 0.1f);

        var ha = NewRect("Handle Slide Area", rt);
        Stretch(ha);
        ha.offsetMin = new Vector2(10, 0); ha.offsetMax = new Vector2(-10, 0);
        var handle = NewRect("Handle", ha);
        handle.anchorMin = new Vector2(0, 0); handle.anchorMax = new Vector2(0, 1);
        handle.sizeDelta = new Vector2(20, 0);
        var hImg = handle.gameObject.AddComponent<Image>(); hImg.color = Color.white;

        var s = rt.gameObject.AddComponent<Slider>();
        s.fillRect = fill;
        s.handleRect = handle;
        s.targetGraphic = hImg;
        s.direction = Slider.Direction.LeftToRight;
        s.minValue = min; s.maxValue = max;
        s.wholeNumbers = true;
        s.value = val;
        s.onValueChanged.AddListener(cb);
        return s;
    }

    Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 size, Color col, UnityAction cb)
    {
        var rt = NewRect("Btn " + label, parent);
        Anchor(rt, new Vector2(0, 1), pos, size);
        var img = rt.gameObject.AddComponent<Image>(); img.color = col;
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(cb);
        var t = MakeText(rt, "Texto", "<b>" + label + "</b>", 24, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Stretch(t.rectTransform);
        return b;
    }

    // ------------------------- Helpers 3D ----------------------------
    static GameObject Prim(string name, PrimitiveType type, Vector3 pos, Vector3 scale, Color c, Transform parent)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().material.color = c;
        return go;
    }

    static LineRenderer MakeLine(string name, Color c, float width, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = c;
        lr.startWidth = lr.endWidth = width;
        lr.useWorldSpace = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return lr;
    }
}

public class TargetPiece : MonoBehaviour
{
    public int points = 50;
    public float moveThreshold = 0.7f;   // m de desplazamiento para considerarla derribada
    public float tiltThreshold = 35f;    // grados de giro para considerarla derribada

    public bool Knocked { get; private set; }
    public int BrokenJoints { get; private set; }

    Vector3 startPos;
    Quaternion startRot;
    bool armed;

    public void Arm()
    {
        startPos = transform.position;
        startRot = transform.rotation;
        Knocked = false;
        BrokenJoints = 0;
        armed = true;
    }

    void FixedUpdate()
    {
        if (!armed || Knocked) return;
        if ((transform.position - startPos).magnitude > moveThreshold ||
            Quaternion.Angle(startRot, transform.rotation) > tiltThreshold)
            Knocked = true;
    }

    // Mensaje de Unity: se llama cuando se rompe un Joint de este objeto
    void OnJointBreak(float breakForce)
    {
        BrokenJoints++;
    }
}

// =====================================================================
//  Sensor del proyectil: reenvía las colisiones al simulador
// =====================================================================
public class ProjectileSensor : MonoBehaviour
{
    public BallisticSimulator owner;

    void OnCollisionEnter(Collision c)
    {
        if (owner != null) owner.RegisterImpact(c);
    }
}
