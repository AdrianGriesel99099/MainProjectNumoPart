// wwwroot/js/car3d.js
//
// 3D part picker for _CarPicker.cshtml. Builds the car via car-body.js — real, visibly painted
// panels generated at runtime from parametric surface data, not a loaded model — and raycasts
// clicks directly against them. Each pickable panel's mesh/group name matches a Models/Part.cs
// member exactly, the same naming contract the old glb-based model used.
//
// The <select> named by data-target-select is the single source of truth: this script only
// ever sets its value and dispatches 'change'. Any page-level code (upload form, tag-selected-
// photos control) keeps working unchanged whether the part came from 3D, the 2D fallback, or
// someone typing into the select directly.
//
// FALLBACK: a highlighted panel on the far side of a rotating model is trivially reachable by
// rotating — fine for picking one part, which is all this does. (Coverage stays 2D permanently;
// see _CarDiagram.cshtml for why.) This only falls back when WebGL itself is unavailable or the
// model fails to load, so the accessible/keyboard path always exists even when 3D works.
(function () {
    function supportsWebGL() {
        try {
            const canvas = document.createElement('canvas');
            return !!(window.WebGLRenderingContext &&
                (canvas.getContext('webgl') || canvas.getContext('experimental-webgl')));
        } catch (e) {
            return false;
        }
    }

    function fallbackTo2D(root) {
        root.querySelector('.car-picker-3d').classList.add('d-none');
        root.querySelector('.car-picker-fallback').classList.remove('d-none');
        const diagram = root.querySelector('.car-diagram');
        if (!diagram) return;
        const select = document.getElementById(root.dataset.targetSelect);
        diagram.addEventListener('carpartselected', (e) => {
            if (select) {
                select.value = e.detail.part || '';
                select.dispatchEvent(new Event('change', { bubbles: true }));
            }
        });
    }

    // Procedural studio environment — ported verbatim from the reference build rather than the
    // box-room-of-emissive-panels approach this file used before. A canvas gradient standing in
    // for a sky (dark ceiling, a horizon band, a darker floor) plus a few soft highlight patches
    // for softbox reflections, mapped equirectangularly and prefiltered through PMREM into a
    // mipmapped radiance map. This — not the geometry — is most of why the earlier version of
    // this picker didn't read as the same car: different environment shapes produce a completely
    // different falloff across the bodywork even with identical geometry and materials.
    function studioEnv(renderer) {
        const c = document.createElement('canvas'); c.width = 1024; c.height = 512;
        const g = c.getContext('2d');
        const sky = g.createLinearGradient(0, 0, 0, 512);
        sky.addColorStop(0.00, '#11151d');
        sky.addColorStop(0.30, '#28313f');
        sky.addColorStop(0.47, '#5a6a80');
        sky.addColorStop(0.502, '#090c12');
        sky.addColorStop(0.72, '#0d1117');
        sky.addColorStop(1.00, '#04060a');
        g.fillStyle = sky; g.fillRect(0, 0, 1024, 512);
        g.filter = 'blur(14px)';
        g.fillStyle = '#ffffff';
        g.fillRect(70, 26, 330, 58);      // long overhead softbox -> body-side highlight
        g.fillRect(548, 18, 360, 50);
        g.fillStyle = '#d5dfec';
        g.fillRect(292, 132, 180, 24);
        g.fillRect(742, 150, 210, 20);
        g.fillStyle = '#ffb877';           // warm low bounce card
        g.fillRect(400, 296, 250, 34);
        g.fillStyle = '#9fc4ec';
        g.fillRect(20, 300, 180, 30);
        g.filter = 'none';
        const tex = new THREE.CanvasTexture(c);
        tex.mapping = THREE.EquirectangularReflectionMapping;
        const pm = new THREE.PMREMGenerator(renderer);
        pm.compileEquirectangularShader();
        const env = pm.fromEquirectangular(tex).texture;
        tex.dispose(); pm.dispose();
        return env;
    }

    // Real floor + grid rather than a CSS gradient behind a transparent canvas — the reference
    // scene composites the car over an actual dark studio floor inside WebGL (so reflections,
    // fog and the contact shadow all agree with each other), not a backdrop painted separately
    // behind it.
    function buildGround() {
        const group = new THREE.Group();
        const groundMat = new THREE.MeshStandardMaterial({ color: 0x0b0f15, roughness: 0.55, metalness: 0.30 });
        const floor = new THREE.Mesh(new THREE.CircleGeometry(30, 80), groundMat);
        floor.rotation.x = -Math.PI / 2; floor.receiveShadow = true;
        group.add(floor);
        const grid = new THREE.GridHelper(48, 48, 0x263140, 0x151b24);
        grid.position.y = 0.002; grid.material.transparent = true; grid.material.opacity = 0.3;
        group.add(grid);
        return group;
    }

    // A soft blob under the car, drawn once into a canvas rather than rendered purely through the
    // shadow map — the shadow map (enabled below) handles direct occlusion from the key light,
    // this adds the tight contact darkening right at the tyre contact patches that a single
    // directional light's shadow tends to leave too faint at this camera distance.
    function buildContactShadow() {
        const size = 256;
        const c = document.createElement('canvas');
        c.width = c.height = size;
        const g = c.getContext('2d');
        const grad = g.createRadialGradient(size / 2, size / 2, 8, size / 2, size / 2, 124);
        grad.addColorStop(0, 'rgba(0,0,0,.80)');
        grad.addColorStop(0.5, 'rgba(0,0,0,.36)');
        grad.addColorStop(1, 'rgba(0,0,0,0)');
        g.fillStyle = grad;
        g.fillRect(0, 0, size, size);

        const mesh = new THREE.Mesh(
            new THREE.PlaneGeometry(5.0, 2.05),
            new THREE.MeshBasicMaterial({
                map: new THREE.CanvasTexture(c),
                transparent: true,
                depthWrite: false // never occlude the car itself
            })
        );
        mesh.rotation.x = -Math.PI / 2;
        mesh.position.y = 0.005;
        return mesh;
    }

    function initPicker(root) {
        if (root.dataset.c3dInit) return;
        root.dataset.c3dInit = '1';

        if (!supportsWebGL() || typeof THREE === 'undefined' || typeof CarBody === 'undefined') {
            fallbackTo2D(root);
            return;
        }

        const select = document.getElementById(root.dataset.targetSelect);
        const wrap = root.querySelector('.car-picker-canvas-wrap');
        const canvas = root.querySelector('.car-picker-canvas');
        const loadingEl = root.querySelector('.car-picker-loading');
        const stage3d = root.querySelector('.car-picker-3d');

        // No alpha:true — the reference renders into an opaque canvas and lets scene.background
        // (below) provide the colour, rather than compositing a transparent canvas over a CSS
        // backdrop standing in for one.
        let renderer;
        try {
            renderer = new THREE.WebGLRenderer({ canvas: canvas, antialias: true });
        } catch (e) {
            fallbackTo2D(root);
            return;
        }

        // outputEncoding / sRGBEncoding, NOT outputColorSpace / SRGBColorSpace: the vendored
        // bundle is the 2021-era UMD build, where the newer colour-space API simply does not
        // exist. Assigning outputColorSpace here would be silently ignored and every colour
        // would render washed out, with nothing in the console to say why.
        renderer.outputEncoding = THREE.sRGBEncoding;
        renderer.toneMapping = THREE.ACESFilmicToneMapping;
        renderer.toneMappingExposure = 1.05;
        renderer.shadowMap.enabled = true;
        renderer.shadowMap.type = THREE.PCFSoftShadowMap;
        // Capped at 2: past that the extra pixels cost real frame time on phones and buy nothing
        // visible on a canvas this size.
        renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));

        const scene = new THREE.Scene();
        // Both #070910 (--void), matching the reference exactly: the environment map and the
        // fog agree on the same colour, so the horizon the environment implies and the fog the
        // camera actually sees blend into one continuous dark room instead of a visible seam.
        scene.background = new THREE.Color(0x070910);
        scene.fog = new THREE.Fog(0x070910, 16, 42);
        const camera = new THREE.PerspectiveCamera(34, 1, 0.1, 200);
        const partMeshes = {}; // Part name -> mesh
        const originalMaterials = new Map();
        let selectedMesh = null;
        let modelRoot = null;

        function resize() {
            const w = wrap.clientWidth;
            // A container inside a display:none ancestor (the vehicle page's collapsible
            // "Pick on car" panel, closed by default) reports clientWidth 0 — sizing the
            // renderer to that locks in a zero-width, unusable canvas with no later trigger to
            // fix it, since only window resize re-measures. Skipping keeps the last good size
            // (or the initial 0 before first paint) instead of committing to a broken one; the
            // panel's own reveal calls root.resize() below once real layout exists.
            if (w === 0) return;
            const h = Math.max(260, Math.round(w * 0.62));
            renderer.setSize(w, h, false);
            camera.aspect = w / h;
            camera.updateProjectionMatrix();
        }

        // Environment + a 4-light rig, both ported verbatim from the reference rather than the
        // single flat hemisphere+directional pair this file used before — that flatter rig is
        // what made the earlier version of this picker look like a different, less dramatic
        // render of the same car. The key light is a real shadow-casting light now (paired with
        // renderer.shadowMap above and castShadow/receiveShadow already set throughout
        // car-body.js's own mesh construction), not simulated entirely through the environment.
        scene.environment = studioEnv(renderer);
        const key = new THREE.DirectionalLight(0xfff2e2, 2.0);
        key.position.set(4.6, 8.0, 4.6);
        key.castShadow = true;
        key.shadow.mapSize.set(2048, 2048);
        key.shadow.camera.left = -5; key.shadow.camera.right = 5;
        key.shadow.camera.top = 5; key.shadow.camera.bottom = -5;
        key.shadow.camera.near = 1; key.shadow.camera.far = 24;
        key.shadow.bias = -0.0011; key.shadow.normalBias = 0.02;
        scene.add(key);
        const fill = new THREE.DirectionalLight(0x92b8e6, 0.5); fill.position.set(-6, 3.2, -5); scene.add(fill);
        const rim = new THREE.DirectionalLight(0xffb173, 0.85); rim.position.set(-4.2, 2.0, 6.2); scene.add(rim);
        scene.add(new THREE.HemisphereLight(0x3d4a5b, 0x05070b, 0.40));
        scene.add(buildGround());

        let yaw = 0.6, pitch = 0.35, dist = 6.5;
        // The drag writes to these; the render loop eases the live values toward them, so
        // releasing after a flick settles rather than stopping dead. Deliberately a damped follow
        // and not velocity-based inertia: it cannot overshoot, cannot drift, and needs no
        // friction constant to tune.
        let targetYaw = yaw, targetPitch = pitch;
        const target = new THREE.Vector3(0, 0.5, 0);

        function updateCamera() {
            camera.position.set(
                target.x + Math.sin(yaw) * Math.cos(pitch) * dist,
                target.y + Math.sin(pitch) * dist,
                target.z + Math.cos(yaw) * Math.cos(pitch) * dist
            );
            camera.lookAt(target);
        }
        updateCamera();

        // Framed from the model's own bounds instead of a hard-coded distance, so the car fills
        // the canvas properly and stays framed if the mesh is ever swapped for a different one.
        function frameModel(box) {
            const sphere = box.getBoundingSphere(new THREE.Sphere());
            target.copy(sphere.center);
            // Fit against the VERTICAL fov, which is the tighter of the two on a landscape
            // canvas, then pull in slightly — the bounding sphere is sized by the car's length,
            // so fitting it exactly leaves a lot of dead air above and below.
            dist = (sphere.radius / Math.sin((camera.fov * Math.PI / 180) / 2)) * 0.78;
            updateCamera();
        }

        // Manual drag rotation rather than vendoring OrbitControls: this only ever needs yaw
        // and a little pitch clamp, so a full controls library would be dead weight.
        let dragging = false, lastX = 0, lastY = 0;
        canvas.addEventListener('pointerdown', (e) => { dragging = true; lastX = e.clientX; lastY = e.clientY; canvas.setPointerCapture(e.pointerId); });
        canvas.addEventListener('pointerup', () => { dragging = false; });
        canvas.addEventListener('pointermove', (e) => {
            if (!dragging) {
                // Hover feedback. Previously the 3D picker had none at all — you clicked blind
                // and only found out which panel you'd hit afterwards, from the <select>. It also
                // does the job the always-on hitbox outlines were doing, but only for the one
                // part you're pointing at, so the car itself stays clean.
                const over = pick(e.clientX, e.clientY);
                setHover(over);
                canvas.style.cursor = over ? 'pointer' : '';
                return;
            }
            setHover(null); // a hover left showing mid-drag just smears across the bodywork
            targetYaw -= (e.clientX - lastX) * 0.01;
            targetPitch = Math.max(0.05, Math.min(1.0, targetPitch + (e.clientY - lastY) * 0.01));
            lastX = e.clientX; lastY = e.clientY;
        });
        canvas.addEventListener('pointerleave', () => setHover(null));

        const raycaster = new THREE.Raycaster();
        function pick(clientX, clientY) {
            const rect = canvas.getBoundingClientRect();
            const mouse = new THREE.Vector2(
                ((clientX - rect.left) / rect.width) * 2 - 1,
                -((clientY - rect.top) / rect.height) * 2 + 1
            );
            raycaster.setFromCamera(mouse, camera);
            const hits = raycaster.intersectObjects(modelRoot ? modelRoot.children : [], true);
            for (const hit of hits) {
                // A part can be several real meshes (FrontBumper is a nose cap plus three body
                // patches — car-body.js groups them), so the ray only ever hits a LEAF mesh, and
                // the part's own name lives on an ANCESTOR group, not necessarily the object the
                // ray struck. Walk up until userData.isPart is set (car-body.js tags exactly the
                // group registered in partMeshes, never a bare leaf), and return THAT — every
                // caller of pick() needs the whole part, not one panel of it.
                let o = hit.object;
                if (o.userData.noPick) continue;
                while (o && o !== modelRoot) {
                    if (o.userData.isPart) return o;
                    o = o.parent;
                }
            }
            return null;
        }

        let downX = 0, downY = 0;
        canvas.addEventListener('pointerdown', (e) => { downX = e.clientX; downY = e.clientY; });
        canvas.addEventListener('pointerup', (e) => {
            // A drag that happened to end over a panel should not count as a click.
            if (Math.abs(e.clientX - downX) > 4 || Math.abs(e.clientY - downY) > 4) { window.__lastClickDebug = 'dragged'; return; }
            const mesh = pick(e.clientX, e.clientY);
            if (mesh) selectPart(mesh.name);
        });

        // Panels are real, already-visible painted meshes now — not invisible alpha-tested boxes
        // that had to be forced opaque to show a highlight at all (the old car.glb hitboxes
        // needed transparent/opacity/alphaTest moved together for exactly that reason). Tinting
        // a real mesh is just an emissive clone, same as the reference's own paint() function.
        // Colours match this app's amber/ice design tokens (wwwroot/css/site.css :root) rather
        // than being independently hardcoded, so the picker's selection state reads as the same
        // "selected" the rest of the UI uses.
        const SELECTED_COLOR = 0xf2a03e; // --amber
        const HOVER_COLOR = 0x76c6ea;    // --ice

        // "mesh" throughout this file means "the group pick() returned", which can wrap several
        // real meshes sharing one part. originalMaterials is keyed per LEAF mesh (not per group)
        // since that is where a material actually lives — and several panels share the exact same
        // THREE.MeshPhysicalMaterial instance (every body-coloured panel points at the one M.paint
        // object car-body.js builds once), so cloning before tinting is load-bearing: mutating a
        // shared material in place would highlight every panel that uses it, not just this one.
        function eachRealMesh(group, fn) {
            group.traverse((o) => { if (o.isMesh) fn(o); });
        }

        function highlight(group) {
            if (selectedMesh) {
                eachRealMesh(selectedMesh, (m) => {
                    if (originalMaterials.has(m)) m.material = originalMaterials.get(m);
                });
            }
            selectedMesh = group;
            if (group) {
                eachRealMesh(group, (m) => {
                    if (!originalMaterials.has(m)) originalMaterials.set(m, m.material);
                    const clone = originalMaterials.get(m).clone();
                    clone.emissive = new THREE.Color(SELECTED_COLOR);
                    clone.emissiveIntensity = 0.55;
                    m.material = clone;
                });
            }
        }

        // Kept strictly separate from highlight(): hover is transient and must never disturb the
        // selected part's material, or moving the mouse away would silently clear a selection the
        // user had already made. Hence the group !== selectedMesh guards on both paths.
        let hoveredMesh = null;
        function setHover(group) {
            if (group === hoveredMesh) return;
            if (hoveredMesh && hoveredMesh !== selectedMesh) {
                eachRealMesh(hoveredMesh, (m) => {
                    if (originalMaterials.has(m)) m.material = originalMaterials.get(m);
                });
            }
            hoveredMesh = group;
            if (group && group !== selectedMesh) {
                eachRealMesh(group, (m) => {
                    if (!originalMaterials.has(m)) originalMaterials.set(m, m.material);
                    const clone = originalMaterials.get(m).clone();
                    clone.emissive = new THREE.Color(HOVER_COLOR);
                    clone.emissiveIntensity = 0.35; // lighter than a selection, so the two never read as the same state
                    m.material = clone;
                });
            }
        }

        function selectPart(name) {
            // Drop any hover styling on the incoming mesh first, so highlight() caches and
            // restores the real material rather than the tinted hover clone.
            setHover(null);
            highlight(partMeshes[name] || null);
            if (select) {
                select.value = name || '';
                select.dispatchEvent(new Event('change', { bubbles: true }));
            }
        }

        // The select is the source of truth, so external changes (e.g. picking "— clear part —"
        // in the bulk-tag dropdown) must update the 3D highlight too, not just the reverse.
        if (select) {
            select.addEventListener('change', () => {
                const name = select.value;
                if (name && partMeshes[name] && partMeshes[name] !== selectedMesh) {
                    highlight(partMeshes[name]);
                } else if (!name) {
                    highlight(null);
                }
            });
        }

        function animate() {
            requestAnimationFrame(animate);
            const dYaw = targetYaw - yaw;
            const dPitch = targetPitch - pitch;
            // Threshold rather than easing forever: below this the movement is sub-pixel, and
            // skipping updateCamera() lets an idle picker settle to a genuinely static scene.
            if (Math.abs(dYaw) > 0.0002 || Math.abs(dPitch) > 0.0002) {
                yaw += dYaw * 0.18;
                pitch += dPitch * 0.18;
                updateCamera();
            }
            renderer.render(scene, camera);
        }

        // Building the car is local, synchronous geometry generation (car-body.js) — there is no
        // network round-trip to time out on any more, so unlike the old glb fetch this only ever
        // needs a try/catch, not a Promise/timeout pair.
        try {
            const built = CarBody.build();
            modelRoot = built.group;
            Object.assign(partMeshes, built.partMeshes);
            scene.add(modelRoot);

            // buildContactShadow()'s plane is a fixed real-world size (5.0 x 2.05, matching the
            // reference exactly) rather than scaled from the model's bounds — car-body.js's
            // geometry is in the same real-world metres the reference itself used, centred at the
            // same origin, so no rescaling is needed for the two to already agree.
            const bounds = new THREE.Box3().setFromObject(modelRoot);
            scene.add(buildContactShadow());

            frameModel(bounds);

            loadingEl.remove();
            resize();
            window.addEventListener('resize', resize);
            animate();
            // Public: callers that reveal this picker from inside a previously display:none
            // container (the vehicle page's "Pick on car" panel) must call this once it becomes
            // visible, since resize() can't measure a width that doesn't exist yet.
            root.resize = resize;
            window.__car3dDebug = { pick, partMeshes, modelRoot, camera, scene, canvas };
        } catch (e) {
            window.__car3dBuildError = e;
            fallbackTo2D(root);
        }
    }

    // Deferred to DOMContentLoaded rather than run immediately: the vehicle page places
    // _CarPicker's <div id="tag-part-panel"> (and this <script> tag with it) BEFORE
    // <select id="tag-part">, which lives further down in #selection-bar. A <script> tag
    // executes the instant the parser reaches it, blocking on the rest of the document — so
    // running initPicker() immediately looked up a select that didn't exist in the DOM yet,
    // captured null, and every select.value assignment after that silently no-op'd forever.
    // Confirmed by checking root.dataset.targetSelect resolved correctly but the cached
    // `select` binding never updated the UI. Waiting for DOMContentLoaded guarantees the whole
    // document — including elements below this script tag — has been parsed first, regardless
    // of where in the markup the tag happens to sit.
    function start() {
        document.querySelectorAll('.car-picker').forEach(initPicker);
    }
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
