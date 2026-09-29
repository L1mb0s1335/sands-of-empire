using System.Collections;
using System.Collections.Generic;
using Runeterra.Core;
using Runeterra.Map;
using UnityEngine;

namespace Runeterra.Units
{
    /// <summary>
    /// Визуал юнита: фигурка в стиле региона, полоска здоровья и анимации —
    /// покой (дыхание), ходьба, удар оружием, выстрел стрелой, гибель.
    /// </summary>
    public class UnitView : MonoBehaviour
    {
        public float stepDuration = 0.22f;

        public Unit Unit { get; private set; }
        public static int Animating { get; private set; }

        /// <summary>Туман войны: виден ли юнит на клетке зрителю (null — видно всё).</summary>
        public static System.Func<Unit, bool> IsShown;
        private bool _shown = true;

        private HexMapView _map;
        private float _hexSize;
        private float _scale;
        private bool _animating;
        private Transform _figure;
        private Transform _weapon;
        private Transform _bar;
        private Transform _barFill;
        private Material _barMaterial;
        private Material _starMaterial;
        private readonly List<GameObject> _stars = new List<GameObject>();
        private Material _material;
        private float _phase;
        private readonly Queue<IEnumerator> _queue = new Queue<IEnumerator>();

        public static UnitView Create(Unit unit, HexMapView map, RegionData region, Material baseMaterial)
        {
            var go = new GameObject($"Unit_{unit.Data.id}_{unit.Id}");
            go.transform.SetParent(map.transform, true);
            var view = go.AddComponent<UnitView>();
            view.Unit = unit;
            view._map = map;
            view._hexSize = map.hexSize;
            view._scale = map.hexSize * 1.5f;
            view._phase = unit.Id * 1.7f;
            view._material = baseMaterial != null ? new Material(baseMaterial) : new Material(Shader.Find("Runeterra/HexTerrain"));

            var parts = UnitFigure.Build(unit.Data, region, view._scale);
            view.Part("Base", go.transform, parts.Base, Vector3.zero);
            view._figure = view.Part("Figure", go.transform, parts.Body, Vector3.zero);
            view._weapon = view.Part("Weapon", view._figure, parts.Weapon, parts.WeaponPivot);
            // Меш оружия построен относительно фигурки, сдвигаем его обратно к шарниру.
            view._weapon.GetChild(0).localPosition = -parts.WeaponPivot;

            go.transform.SetPositionAndRotation(map.SurfacePosition(unit.Coord), Quaternion.Euler(0f, 180f, 0f));
            view.CreateHealthBar(baseMaterial);
            unit.Moved += view.OnMoved;
            unit.Attacked += view.OnAttacked;
            unit.Damaged += view.OnDamaged;
            unit.Died += view.OnDied;
            unit.Promoted += view.OnPromoted;
            return view;
        }

        /// <summary>Узел с мешем. Для оружия — пустой шарнир с дочерним мешем.</summary>
        private Transform Part(string name, Transform parent, Mesh mesh, Vector3 pivot)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false);
            node.localPosition = pivot;
            var holder = pivot == Vector3.zero ? node : new GameObject(name + "Mesh").transform;
            if (holder != node) holder.SetParent(node, false);
            holder.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            holder.gameObject.AddComponent<MeshRenderer>().sharedMaterial = _material;
            return node;
        }

        private void OnDestroy()
        {
            if (_animating) Animating--;
            if (Unit == null) return;
            Unit.Moved -= OnMoved;
            Unit.Attacked -= OnAttacked;
            Unit.Damaged -= OnDamaged;
            Unit.Died -= OnDied;
            Unit.Promoted -= OnPromoted;
        }

        private void Update()
        {
            bool shown = IsShown == null || IsShown(Unit);
            if (shown != _shown)
            {
                _shown = shown;
                foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = shown;
            }
            // Дыхание в покое.
            if (_figure == null || !Unit.IsAlive) return;
            float t = Time.time * 2.2f + _phase;
            _figure.localPosition = Vector3.up * (Mathf.Sin(t) * 0.006f * _scale);
            _figure.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 0.5f) * 3f, 0f);
        }

        private void LateUpdate()
        {
            var cam = _map != null ? _map.mapCamera : null;
            if (cam != null && _bar != null) _bar.rotation = cam.transform.rotation;
        }

        // ---------- Здоровье ----------

        private void CreateHealthBar(Material baseMaterial)
        {
            _bar = new GameObject("HealthBar").transform;
            _bar.SetParent(transform, false);
            _bar.localPosition = Vector3.up * 0.95f * _hexSize;

            var bg = MakeQuad(_bar, baseMaterial, new Color(0.1f, 0.1f, 0.1f), 0f);
            bg.localScale = new Vector3(0.5f, 0.07f, 1f) * _hexSize;

            var fillPivot = new GameObject("Fill").transform;
            fillPivot.SetParent(_bar, false);
            fillPivot.localPosition = new Vector3(-0.24f * _hexSize, 0f, -0.001f);
            var fill = MakeQuad(fillPivot, baseMaterial, Color.green, 0.5f);
            fill.localScale = new Vector3(0.48f, 0.05f, 1f) * _hexSize;
            fill.localPosition = new Vector3(0.24f * _hexSize, 0f, 0f);
            _barFill = fillPivot;
            _barMaterial = fill.GetComponent<Renderer>().sharedMaterial;
            _starMaterial = baseMaterial;
            OnDamaged(Unit);
            OnPromoted(Unit);
        }

        /// <summary>Звёздочки звания слева от полоски здоровья.</summary>
        private void OnPromoted(Unit unit)
        {
            foreach (var star in _stars) Destroy(star);
            _stars.Clear();
            for (int i = 0; i < unit.Level; i++)
            {
                var q = MakeQuad(_bar, _starMaterial, new Color(1f, 0.82f, 0.2f), 0.8f);
                q.localScale = new Vector3(0.07f, 0.07f, 1f) * _hexSize;
                q.localPosition = new Vector3((-0.3f - i * 0.09f) * _hexSize, 0f, -0.002f);
                q.localRotation = Quaternion.Euler(0f, 0f, 45f);
                _stars.Add(q.gameObject);
            }
        }

        private static Transform MakeQuad(Transform parent, Material baseMaterial, Color color, float emission)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            var mat = baseMaterial != null ? new Material(baseMaterial) : new Material(Shader.Find("Runeterra/HexTerrain"));
            mat.color = color;
            mat.SetColor("_EmissionColor", color * emission);
            var r = quad.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return quad.transform;
        }

        private void OnDamaged(Unit unit)
        {
            if (_barFill == null) return;
            float t = (float)unit.Health / unit.Data.maxHealth;
            _barFill.localScale = new Vector3(Mathf.Max(0.001f, t), 1f, 1f);
            var c = t > 0.6f ? new Color(0.3f, 0.85f, 0.3f) : t > 0.3f ? new Color(0.95f, 0.8f, 0.2f) : new Color(0.9f, 0.25f, 0.2f);
            _barMaterial.color = c;
            _barMaterial.SetColor("_EmissionColor", c * 0.5f);
        }

        // ---------- События юнита ----------

        private void OnMoved(Unit unit, IReadOnlyList<HexCoord> path)
        {
            if (!Application.isPlaying)
            {
                transform.position = _map.SurfacePosition(unit.Coord);
                return;
            }
            Enqueue(Walk(path));
        }

        private void OnAttacked(Unit unit, HexCoord target)
        {
            if (!Application.isPlaying || !isActiveAndEnabled) return;
            Enqueue(unit.Data.IsRanged ? Shoot(target) : Strike(target));
        }

        private void OnDied(Unit unit)
        {
            if (_bar != null) _bar.gameObject.SetActive(false);
            if (!Application.isPlaying) { DestroyImmediate(gameObject); return; }
            Enqueue(unit.Consumed ? Vanish() : Fall());
        }

        // ---------- Анимации ----------

        private void Enqueue(IEnumerator action)
        {
            _queue.Enqueue(action);
            if (!_animating) StartCoroutine(RunQueue());
        }

        private IEnumerator RunQueue()
        {
            _animating = true;
            Animating++;
            while (_queue.Count > 0) yield return StartCoroutine(_queue.Dequeue());
            _animating = false;
            Animating--;
        }

        private void Face(Vector3 target)
        {
            var flat = target - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(flat);
        }

        private IEnumerator Walk(IReadOnlyList<HexCoord> path)
        {
            foreach (var step in path)
            {
                var from = transform.position;
                var to = _map.SurfacePosition(step);
                Face(to);
                for (float t = 0f; t < 1f; t += Time.deltaTime / stepDuration)
                {
                    var p = Vector3.Lerp(from, to, t);
                    p.y += Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f)) * 0.03f * _hexSize;
                    transform.position = p;
                    _weapon.localRotation = Quaternion.Euler(Mathf.Sin(t * Mathf.PI * 2f) * 15f, 0f, 0f);
                    yield return null;
                }
                transform.position = to;
            }
            _weapon.localRotation = Quaternion.identity;
        }

        /// <summary>Выпад и взмах оружием: замах назад, удар вперёд, возврат.</summary>
        private IEnumerator Strike(HexCoord targetCoord)
        {
            var target = _map.SurfacePosition(targetCoord);
            var home = transform.position;
            Face(target);
            const float duration = 0.45f;
            for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
            {
                float lunge = Mathf.Sin(Mathf.Clamp01((t - 0.2f) / 0.6f) * Mathf.PI);
                transform.position = Vector3.Lerp(home, Vector3.Lerp(home, target, 0.4f), lunge);
                float angle = t < 0.35f ? Mathf.Lerp(0f, -70f, t / 0.35f)
                    : t < 0.6f ? Mathf.Lerp(-70f, 80f, (t - 0.35f) / 0.25f)
                    : Mathf.Lerp(80f, 0f, (t - 0.6f) / 0.4f);
                _weapon.localRotation = Quaternion.Euler(angle, 0f, 0f);
                yield return null;
            }
            transform.position = home;
            _weapon.localRotation = Quaternion.identity;
        }

        /// <summary>Натянуть и отпустить: из лучника по дуге летит стрела.</summary>
        private IEnumerator Shoot(HexCoord targetCoord)
        {
            var target = _map.SurfacePosition(targetCoord) + Vector3.up * 0.3f * _hexSize;
            Face(target);
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.25f)
            {
                _weapon.localRotation = Quaternion.Euler(Mathf.Lerp(0f, -35f, t), 0f, 0f);
                yield return null;
            }
            _weapon.localRotation = Quaternion.identity;
            yield return Projectile.Fly(transform.position + Vector3.up * 0.45f * _hexSize, target, _material, _hexSize);
        }

        /// <summary>Гибель: фигурка падает навзничь и уходит в землю.</summary>
        private IEnumerator Fall()
        {
            yield return new WaitForSeconds(0.2f);
            var start = transform.position;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.5f)
            {
                _figure.localRotation = Quaternion.Euler(-90f * Mathf.SmoothStep(0f, 1f, t), 0f, 0f);
                yield return null;
            }
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.4f)
            {
                transform.position = start + Vector3.down * 0.3f * _hexSize * t;
                yield return null;
            }
            Destroy(gameObject);
        }

        /// <summary>Строитель израсходован: фигурка тает.</summary>
        private IEnumerator Vanish()
        {
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.4f)
            {
                transform.localScale = Vector3.one * (1f - t);
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
