// wwwroot/js/car-body.js
//
// Procedurally generates the 3D car body used by the picker — real, visibly painted panels
// that are ALSO the click targets, not invisible boxes laid over a separately-loaded mesh.
// Adapted from a four-door-saloon reference build supplied for this exact purpose: the surface
// math (station-table cross-sections, greenhouse loft) and every panel's tuned coordinates are
// kept as close to verbatim as this codebase's conventions allow, rather than re-derived —
// those numbers were arrived at through real iteration (the comments below inline several of
// the documented fixes) and re-deriving them from scratch would just reproduce the same bugs.
//
// window.CarBody.build() returns { group, partMeshes, dispose }. partMeshes is keyed by exact
// Part enum member names (Models/Part.cs) for every one of the 72 panels the reference itself
// treats as a real, individually clickable part — matching it exactly, not just the smaller set
// this app originally tracked. Only pure scenery the reference ALSO never made clickable (the
// body shell, cabin tub, wheel-house liner backing, beltline seal, glass frit) stays flagged
// userData.noPick, the same as it does there.
(function (global) {
    'use strict';

    function pchip(xs, ys) {
        const n = xs.length, h = [], d = [];
        for (let i = 0; i < n - 1; i++) { h[i] = xs[i + 1] - xs[i]; d[i] = (ys[i + 1] - ys[i]) / h[i]; }
        const m = new Array(n); m[0] = d[0]; m[n - 1] = d[n - 2];
        for (let i = 1; i < n - 1; i++) {
            if (d[i - 1] * d[i] <= 0) m[i] = 0;
            else { const w1 = 2 * h[i] + h[i - 1], w2 = h[i] + 2 * h[i - 1]; m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i]); }
        }
        return function (x) {
            if (x <= xs[0]) return ys[0];
            if (x >= xs[n - 1]) return ys[n - 1];
            let i = 0; while (i < n - 2 && x > xs[i + 1]) i++;
            const t = (x - xs[i]) / h[i], t2 = t * t, t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * ys[i] + (t3 - 2 * t2 + t) * h[i] * m[i] + (-2 * t3 + 3 * t2) * ys[i + 1] + (t3 - t2) * h[i] * m[i + 1];
        };
    }

    function smoothstep(a, b, v) { const t = Math.min(1, Math.max(0, (v - a) / (b - a))); return t * t * (3 - 2 * t); }
    function neg(t) { return (typeof t === 'function') ? (f => -t(f)) : -t; }

    // Station table: x (metres, nose +ve), top(centre crown), belt(shoulder), hwB(half width @
    // shoulder), hwM(max half width), midY(height of max width), rkW/rkY(sill), unY(underbody).
    // n1/n2 override the two crown intermediates (shapes the cabin tub). Verbatim from the
    // reference — this is measurement data, not creative expression, and hand-retyping it risks
    // a single transposed digit producing a visibly dented panel with no obvious cause.
    const ST = [
        {x: 2.280, top:.530, belt:.512, hwB:.250, hwM:.265, midY:.400, rkW:.230, rkY:.290, unY:.300},
        {x: 2.262, top:.546, belt:.527, hwB:.286, hwM:.302, midY:.390, rkW:.263, rkY:.256, unY:.270},
        {x: 2.238, top:.570, belt:.550, hwB:.352, hwM:.372, midY:.380, rkW:.324, rkY:.218, unY:.234},
        {x: 2.212, top:.588, belt:.568, hwB:.424, hwM:.444, midY:.374, rkW:.387, rkY:.202, unY:.217},
        {x: 2.180, top:.610, belt:.589, hwB:.508, hwM:.530, midY:.372, rkW:.463, rkY:.192, unY:.206},
        {x: 2.150, top:.640, belt:.616, hwB:.592, hwM:.616, midY:.376, rkW:.540, rkY:.186, unY:.200},
        {x: 2.100, top:.672, belt:.646, hwB:.692, hwM:.719, midY:.386, rkW:.630, rkY:.181, unY:.195},
        {x: 2.050, top:.694, belt:.665, hwB:.734, hwM:.760, midY:.393, rkW:.667, rkY:.179, unY:.195},
        {x: 2.000, top:.712, belt:.682, hwB:.764, hwM:.791, midY:.400, rkW:.694, rkY:.178, unY:.195},
        {x: 1.900, top:.750, belt:.717, hwB:.786, hwM:.811, midY:.418, rkW:.712, rkY:.178, unY:.200},
        {x: 1.760, top:.790, belt:.752, hwB:.806, hwM:.828, midY:.442, rkW:.730, rkY:.180, unY:.218},
        {x: 1.560, top:.831, belt:.782, hwB:.824, hwM:.845, midY:.466, rkW:.752, rkY:.180, unY:.242},
        {x: 1.290, top:.862, belt:.812, hwB:.836, hwM:.852, midY:.486, rkW:.762, rkY:.171, unY:.247},
        {x: 1.020, top:.886, belt:.840, hwB:.853, hwM:.864, midY:.490, rkW:.779, rkY:.164, unY:.237},
        {x: 0.900, top:.900, belt:.848, hwB:.852, hwM:.862, midY:.492, rkW:.778, rkY:.162, unY:.234},
        {x: 0.870, top:.910, belt:.853, hwB:.851, hwM:.861, midY:.493, rkW:.778, rkY:.161, unY:.233},
        {x: 0.850, top:.916, belt:.856, hwB:.850, hwM:.860, midY:.493, rkW:.778, rkY:.161, unY:.232, n1:.902, n2:.892},
        {x: 0.820, top:.700, belt:.862, hwB:.849, hwM:.859, midY:.494, rkW:.777, rkY:.160, unY:.231, n1:.800, n2:.872},
        {x: 0.760, top:.545, belt:.872, hwB:.847, hwM:.856, midY:.494, rkW:.776, rkY:.159, unY:.230, n1:.660, n2:.858},
        {x: 0.640, top:.470, belt:.884, hwB:.844, hwM:.853, midY:.495, rkW:.775, rkY:.158, unY:.228, n1:.615, n2:.855},
        {x: 0.480, top:.450, belt:.892, hwB:.841, hwM:.850, midY:.495, rkW:.773, rkY:.158, unY:.227, n1:.605, n2:.852},
        {x: 0.300, top:.442, belt:.900, hwB:.838, hwM:.848, midY:.496, rkW:.772, rkY:.157, unY:.226, n1:.600, n2:.856},
        {x: 0.000, top:.430, belt:.910, hwB:.836, hwM:.847, midY:.497, rkW:.771, rkY:.157, unY:.225, n1:.595, n2:.865},
        {x:-0.400, top:.430, belt:.920, hwB:.836, hwM:.848, midY:.498, rkW:.771, rkY:.158, unY:.225, n1:.600, n2:.875},
        {x:-0.720, top:.446, belt:.930, hwB:.840, hwM:.852, midY:.500, rkW:.772, rkY:.160, unY:.227, n1:.615, n2:.885},
        {x:-1.000, top:.560, belt:.941, hwB:.846, hwM:.858, midY:.503, rkW:.774, rkY:.164, unY:.233, n1:.700, n2:.896},
        {x:-1.235, top:.700, belt:.958, hwB:.852, hwM:.864, midY:.508, rkW:.776, rkY:.171, unY:.243, n1:.800, n2:.912},
        {x:-1.420, top:.880, belt:.976, hwB:.854, hwM:.865, midY:.512, rkW:.775, rkY:.181, unY:.253, n1:.930, n2:.955},
        {x:-1.500, top:1.005, belt:.982, hwB:.826, hwM:.842, midY:.514, rkW:.755, rkY:.189, unY:.259},
        {x:-1.560, top:.990, belt:.968, hwB:.822, hwM:.838, midY:.515, rkW:.752, rkY:.195, unY:.263},
        {x:-1.750, top:.940, belt:.916, hwB:.800, hwM:.817, midY:.515, rkW:.733, rkY:.209, unY:.273},
        {x:-1.950, top:.920, belt:.898, hwB:.780, hwM:.797, midY:.512, rkW:.715, rkY:.226, unY:.286},
        {x:-2.100, top:.910, belt:.886, hwB:.750, hwM:.768, midY:.505, rkW:.688, rkY:.246, unY:.301},
        {x:-2.180, top:.900, belt:.876, hwB:.698, hwM:.715, midY:.502, rkW:.640, rkY:.258, unY:.312},
        {x:-2.240, top:.880, belt:.858, hwB:.585, hwM:.600, midY:.500, rkW:.536, rkY:.272, unY:.326},
        {x:-2.270, top:.860, belt:.840, hwB:.522, hwM:.535, midY:.500, rkW:.478, rkY:.286, unY:.340},
        {x:-2.280, top:.840, belt:.822, hwB:.512, hwM:.524, midY:.500, rkW:.470, rkY:.302, unY:.354}
    ];
    const CH = {};
    (function buildCurves() {
        const xs = ST.map(s => s.x).slice().reverse();
        const keys = ['top', 'belt', 'hwB', 'hwM', 'midY', 'rkW', 'rkY', 'unY', 'n1', 'n2'];
        for (const k of keys) {
            const ys = ST.map(s => {
                if (s[k] !== undefined) return s[k];
                if (k === 'n1') return s.belt + (s.top - s.belt) * 0.87;
                if (k === 'n2') return s.belt + (s.top - s.belt) * 0.44;
                return 0;
            }).slice().reverse();
            CH[k] = pchip(xs, ys);
        }
    })();

    const AXLE_F = 1.290, AXLE_R = -1.235, WHEEL_Y = 0.315, ARCH_R = 0.378;
    const secCache = new Map();
    function sectionAt(x) {
        const k = Math.round(x * 4000);
        let c = secCache.get(k);
        if (c) return c;
        const top = CH.top(x), belt = CH.belt(x), hwB = CH.hwB(x), hwM = CH.hwM(x),
            midY = CH.midY(x), rkW = CH.rkW(x), rkY = CH.rkY(x), unY = CH.unY(x),
            n1 = CH.n1(x), n2 = CH.n2(x);
        const hwS = Math.min(hwB, hwM * 0.912);
        const midS = midY + 0.075 * (hwM / 0.865);
        const S = [0, .16, .30, .40, .46, .60, .78, .88, .94, 1];
        const Z = [0, .52 * hwS, .86 * hwS, .985 * hwS, hwS, hwM, rkW + .022, rkW, rkW * .62, 0];
        const Y = [top, n1, n2, belt + .006, belt - .010, midS, rkY + .065, rkY, unY + .014, unY];
        let a = 0, ay = 0;
        for (const wx of [AXLE_F, AXLE_R]) {
            const dx = x - wx;
            if (Math.abs(dx) < ARCH_R) {
                const yy = WHEEL_Y + Math.sqrt(ARCH_R * ARCH_R - dx * dx);
                const fadeH = (yy - (rkY + .030)) / .075;
                const fadeX = (ARCH_R - Math.abs(dx)) / .090;
                let kk = Math.min(1, Math.max(0, Math.min(fadeH, fadeX)));
                kk = kk * kk * (3 - 2 * kk);
                if (kk > a) { a = kk; ay = yy; }
            }
        }
        if (a > 0) {
            const iz = .615, L = (p, q) => p + (q - p) * a;
            Y[5] = L(Y[5], ay + .048);
            Z[6] = L(Z[6], hwM - .008); Y[6] = L(Y[6], ay + .004);
            Z[7] = L(Z[7], iz); Y[7] = L(Y[7], ay - .055);
            Z[8] = L(Z[8], iz * .8); Y[8] = L(Y[8], unY + .062);
        }
        c = { fz: pchip(S, Z), fy: pchip(S, Y) };
        secCache.set(k, c);
        return c;
    }
    function pointAt(x, t, out) {
        let s = t, side = 1;
        if (s < 0) { s = -s; side = -1; }
        if (s > 1) { s = 2 - s; side = -side; }
        if (s < 0) { s = -s; side = -side; }
        const c = sectionAt(x);
        out = out || new THREE.Vector3();
        return out.set(x, c.fy(s), side * c.fz(s));
    }
    const _a = new THREE.Vector3(), _b = new THREE.Vector3(), _c = new THREE.Vector3(), _d = new THREE.Vector3();
    function normalAt(x, t, out) {
        const h = .005, dt = .006;
        pointAt(x + h, t, _a); pointAt(x - h, t, _b); _a.sub(_b);
        pointAt(x, t + dt, _c); pointAt(x, t - dt, _d); _c.sub(_d);
        out = out || new THREE.Vector3();
        out.crossVectors(_c, _a);
        if (out.lengthSq() < 1e-12) out.set(0, 1, 0); else out.normalize();
        return out;
    }

    function surfPatch(o, PT, NM) {
        const x0 = o.x0, x1 = o.x1;
        const nx = o.nx || Math.max(2, Math.ceil(Math.abs(x1 - x0) / (o.dx || 0.030)) + 1);
        const t0f = typeof o.t0 === 'function' ? o.t0 : (() => o.t0);
        const t1f = typeof o.t1 === 'function' ? o.t1 : (() => o.t1);
        const nt = o.nt || Math.max(2, Math.ceil(Math.abs(t1f(1) - t0f(1)) / (o.dt || 0.022)) + 1);
        const offf = typeof o.off === 'function' ? o.off : (() => (o.off || 0));
        const pos = new Float32Array(nx * nt * 3), nor = new Float32Array(nx * nt * 3), uv = new Float32Array(nx * nt * 2);
        const p = new THREE.Vector3(), n = new THREE.Vector3();
        let w = 0;
        for (let i = 0; i < nx; i++) {
            const fx = i / (nx - 1), x = x0 + (x1 - x0) * fx, a = t0f(fx), b = t1f(fx);
            for (let k = 0; k < nt; k++) {
                const ft = k / (nt - 1), t = a + (b - a) * ft, of = offf(fx, ft);
                PT(x, t, p); NM(x, t, n);
                const j = w * 3;
                pos[j] = p.x + n.x * of; pos[j + 1] = p.y + n.y * of; pos[j + 2] = p.z + n.z * of;
                nor[j] = n.x; nor[j + 1] = n.y; nor[j + 2] = n.z;
                uv[w * 2] = fx * (o.ur || 1); uv[w * 2 + 1] = ft * (o.vr || 1);
                w++;
            }
        }
        const idx = [];
        for (let i = 0; i < nx - 1; i++) for (let k = 0; k < nt - 1; k++) {
            const A = i * nt + k, B = A + nt, C = B + 1, D = A + 1; idx.push(A, B, C, A, C, D);
        }
        for (let i = 0; i < idx.length; i += 3) {
            const A = idx[i] * 3, B = idx[i + 1] * 3, C = idx[i + 2] * 3;
            const ux = pos[B] - pos[A], uy = pos[B + 1] - pos[A + 1], uz = pos[B + 2] - pos[A + 2];
            const vx = pos[C] - pos[A], vy = pos[C + 1] - pos[A + 1], vz = pos[C + 2] - pos[A + 2];
            const fx2 = uy * vz - uz * vy, fy2 = uz * vx - ux * vz, fz2 = ux * vy - uy * vx;
            const nx2 = nor[A] + nor[B] + nor[C], ny2 = nor[A + 1] + nor[B + 1] + nor[C + 1], nz2 = nor[A + 2] + nor[B + 2] + nor[C + 2];
            if (fx2 * nx2 + fy2 * ny2 + fz2 * nz2 < 0) { const t = idx[i + 1]; idx[i + 1] = idx[i + 2]; idx[i + 2] = t; }
        }
        const g = new THREE.BufferGeometry();
        g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
        g.setAttribute('normal', new THREE.BufferAttribute(nor, 3));
        g.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
        g.setIndex(idx);
        const m = new THREE.Mesh(g, o.mat);
        m.castShadow = o.shadow !== false; m.receiveShadow = o.shadow !== false;
        return m;
    }
    function bp(o) { return surfPatch(o, pointAt, normalAt); }
    function bpPair(o) { return [bp(o), bp(Object.assign({}, o, { t0: neg(o.t0), t1: neg(o.t1) }))]; }
    function place(obj, x, t, lift) {
        const p = pointAt(x, t), n = normalAt(x, t);
        obj.position.copy(p).addScaledVector(n, lift || 0);
        obj.lookAt(p.clone().addScaledVector(n, 1));
        return obj;
    }

    // ---------- greenhouse loft (windscreen / roof / backlight) ----------
    // A second, independent surface for the glasshouse: the body loft above stops at the
    // beltline (S_BASE), and the roof/glass sit on their own curve above that, blended down to
    // meet the body curve exactly at the beltline so the two surfaces share one seam rather than
    // merely lining up by coincidence.
    const GH = [
        {x: 0.850, ry:.916, hw:.740}, {x: 0.800, ry:.933, hw:.736}, {x: 0.700, ry:.985, hw:.730},
        {x: 0.600, ry:1.040,hw:.722}, {x: 0.500, ry:1.092,hw:.712}, {x: 0.400, ry:1.145,hw:.702},
        {x: 0.300, ry:1.192,hw:.690}, {x: 0.200, ry:1.234,hw:.676}, {x: 0.100, ry:1.266,hw:.662},
        {x: 0.030, ry:1.281,hw:.652}, {x:-0.050, ry:1.289,hw:.646}, {x:-0.150, ry:1.293,hw:.641},
        {x:-0.300, ry:1.295,hw:.638}, {x:-0.600, ry:1.295,hw:.638}, {x:-0.800, ry:1.286,hw:.640},
        {x:-0.880, ry:1.268,hw:.645}, {x:-1.000, ry:1.226,hw:.652}, {x:-1.150, ry:1.170,hw:.663},
        {x:-1.300, ry:1.108,hw:.680}, {x:-1.420, ry:1.052,hw:.706}, {x:-1.505, ry:1.000,hw:.734}
    ];
    const GHC = {};
    (function () {
        const xs = GH.map(g => g.x).slice().reverse();
        GHC.ry = pchip(xs, GH.map(g => g.ry).slice().reverse());
        GHC.hw = pchip(xs, GH.map(g => g.hw).slice().reverse());
    })();
    const GH_X0 = GH[0].x, GH_X1 = GH[GH.length - 1].x;
    // Must equal the door/quarter top-edge parameter used in buildGreenhouse below — this is
    // literally the same point on the body, sampled by two different systems; a mismatch here is
    // a steady gap running the full length of the car.
    const S_BASE = 0.390;
    const ghCache = new Map();
    function ghSection(x) {
        const k = Math.round(x * 4000);
        let c = ghCache.get(k); if (c) return c;
        x = Math.min(GH_X0, Math.max(GH_X1, x));
        const sec = sectionAt(x);
        const zb = sec.fz(S_BASE), yb = sec.fy(S_BASE);
        const ry = Math.max(GHC.ry(x), yb), hw = Math.min(GHC.hw(x), zb);
        const drop = ry - yb;
        const U = [0, .30, .52, .64, .82, 1];
        const Z = [0, .60 * hw, .93 * hw, hw, zb + .012, zb];
        const Y = [ry, ry - Math.min(.012, .10 * drop), ry - Math.min(.032, .20 * drop), ry - Math.min(.058, .34 * drop), yb + .50 * drop, yb];
        c = { fz: pchip(U, Z), fy: pchip(U, Y) };
        ghCache.set(k, c); return c;
    }
    function ghPoint(x, u, out) {
        let s = u, side = 1;
        if (s < 0) { s = -s; side = -1; }
        s = Math.min(1.0, s);
        const c = ghSection(x);
        out = out || new THREE.Vector3();
        return out.set(x, c.fy(s), side * c.fz(s));
    }
    const _e = new THREE.Vector3();
    function ghNormal(x, u, out) {
        const h = .006, du = .008;
        ghPoint(x + h, u, _a); ghPoint(x - h, u, _b); _a.sub(_b);
        ghPoint(x, u + du, _c); ghPoint(x, u - du, _e); _c.sub(_e);
        out = out || new THREE.Vector3();
        out.crossVectors(_c, _a);
        if (out.lengthSq() < 1e-12) out.set(0, 1, 0); else out.normalize();
        return out;
    }
    function gp(o) { return surfPatch(o, ghPoint, ghNormal); }
    function gpPair(o) { return [gp(o), gp(Object.assign({}, o, { t0: neg(o.t0), t1: neg(o.t1) }))]; }
    function placeGH(obj, x, t, lift) {
        const p = ghPoint(x, t), n = ghNormal(x, t);
        obj.position.copy(p).addScaledVector(n, lift || 0);
        obj.lookAt(p.clone().addScaledVector(n, 1));
        return obj;
    }

    const WS_X0 = 1.100, WS_X1 = 0.250, RF_X1 = -1.150, BL_X1 = -1.560;
    // EDGE_IN / EDGE_OUT are the two boundary curves every greenhouse patch reads from — glass
    // and roof sit inboard of EDGE_IN, pillar/rail between EDGE_IN and EDGE_OUT, door and quarter
    // glass outboard of EDGE_OUT. Every patch deriving its border from these SAME functions
    // (evaluated at its own absolute x) is what makes adjacent patches meet exactly rather than
    // merely by luck.
    const EDGE_IN = pchip(
        [1.100, 0.780, 0.480, 0.250, -0.300, -0.700, -1.150, -1.300, -1.430, -1.560].slice().reverse(),
        [0.800, 0.742, 0.688, 0.646, 0.638, 0.636, 0.640, 0.622, 0.556, 0.492].slice().reverse());
    const EDGE_OUT = pchip(
        [1.100, 0.780, 0.480, 0.250, -0.300, -0.700, -1.150, -1.300].slice().reverse(),
        [0.960, 0.874, 0.758, 0.684, 0.666, 0.663, 0.670, 0.678].slice().reverse());
    function edgeIn(x0, x1) { return f => EDGE_IN(x0 + (x1 - x0) * f); }
    function edgeOut(x0, x1) { return f => EDGE_OUT(x0 + (x1 - x0) * f); }
    const uWi = f => EDGE_IN(WS_X0 + (WS_X1 - WS_X0) * f);
    const uPo = f => EDGE_OUT(WS_X0 + (WS_X1 - WS_X0) * f);
    function capMesh(xTip, dome, mat, nr, nt) {
        nr = nr || 10; nt = nt || 60;
        const ring = []; let cy = 0;
        for (let k = 0; k <= nt; k++) {
            const t = -1 + 2 * k / nt;
            const p = pointAt(xTip, t, new THREE.Vector3());
            ring.push(p); cy += p.y;
        }
        cy /= (nt + 1);
        const pos = [], idx = [];
        for (let i = 0; i <= nr; i++) {
            const rho = i / nr;
            for (let k = 0; k <= nt; k++) {
                const r = ring[k];
                pos.push(xTip + dome * (1 - rho * rho), cy + (r.y - cy) * rho, r.z * rho);
            }
        }
        for (let i = 0; i < nr; i++) for (let k = 0; k < nt; k++) {
            const A = i * (nt + 1) + k, Bq = A + nt + 1, C = Bq + 1, D = A + 1;
            if (i === 0) idx.push(A, Bq, C);
            else idx.push(A, Bq, C, A, C, D);
        }
        const ax = Math.sign(dome) || 1;
        for (let i = 0; i < idx.length; i += 3) {
            const A = idx[i] * 3, B = idx[i + 1] * 3, C = idx[i + 2] * 3;
            const uy = pos[B + 1] - pos[A + 1], uz = pos[B + 2] - pos[A + 2];
            const vy = pos[C + 1] - pos[A + 1], vz = pos[C + 2] - pos[A + 2];
            if ((uy * vz - uz * vy) * ax < 0) { const t = idx[i + 1]; idx[i + 1] = idx[i + 2]; idx[i + 2] = t; }
        }
        const g = new THREE.BufferGeometry();
        g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
        g.setIndex(idx); g.computeVertexNormals();
        const m = new THREE.Mesh(g, mat);
        m.castShadow = true; m.receiveShadow = true;
        return m;
    }

    function mkMesh() {
        const c = document.createElement('canvas'); c.width = c.height = 64;
        const g = c.getContext('2d');
        g.fillStyle = '#05060a'; g.fillRect(0, 0, 64, 64);
        g.strokeStyle = '#2b3038'; g.lineWidth = 2.5;
        for (let i = 4; i < 64; i += 9) {
            g.beginPath(); g.moveTo(i, 0); g.lineTo(i, 64); g.stroke();
            g.beginPath(); g.moveTo(0, i); g.lineTo(64, i); g.stroke();
        }
        const t = new THREE.CanvasTexture(c);
        t.wrapS = t.wrapT = THREE.RepeatWrapping; t.repeat.set(12, 3);
        return t;
    }
    function mkTread() {
        const c = document.createElement('canvas'); c.width = 64; c.height = 128;
        const g = c.getContext('2d');
        g.fillStyle = '#101216'; g.fillRect(0, 0, 64, 128);
        g.fillStyle = '#05070a';
        g.fillRect(6, 0, 4, 128); g.fillRect(28, 0, 7, 128); g.fillRect(52, 0, 4, 128);
        g.fillStyle = '#080a0e';
        for (let i = 0; i < 128; i += 14) { g.fillRect(12, i, 14, 7); g.fillRect(38, i + 7, 12, 7); }
        const t = new THREE.CanvasTexture(c);
        t.wrapS = t.wrapT = THREE.RepeatWrapping; t.repeat.set(52, 1);
        return t;
    }

    // Alpine Silver — PAINTS[0] in the reference's own paint-swatch list (six colours; this app
    // has no paint-swatch UI yet, so the default is what's used, verbatim).
    const PAINT_DEFAULT = { hex: 0xB9BEC4, metal: 0.86, rough: 0.20 };

    function buildMaterials() {
        const M = {
            paint: new THREE.MeshPhysicalMaterial({ color: PAINT_DEFAULT.hex, metalness: PAINT_DEFAULT.metal, roughness: PAINT_DEFAULT.rough, clearcoat: 1, clearcoatRoughness: .05, envMapIntensity: 1.5 }),
            glass: new THREE.MeshPhysicalMaterial({ color: 0x121a22, metalness: .12, roughness: .045, transparent: true, opacity: .44, envMapIntensity: 2.6, side: THREE.DoubleSide, depthWrite: false }),
            glassD: new THREE.MeshPhysicalMaterial({ color: 0x0b1016, metalness: .12, roughness: .05, transparent: true, opacity: .55, envMapIntensity: 2.3, side: THREE.DoubleSide, depthWrite: false }),
            shell: new THREE.MeshStandardMaterial({ color: 0x0d1015, metalness: .3, roughness: .85, side: THREE.DoubleSide }),
            black: new THREE.MeshStandardMaterial({ color: 0x0b0d11, metalness: .25, roughness: .68 }),
            rubber: new THREE.MeshStandardMaterial({ color: 0x0a0c0f, metalness: .02, roughness: .95 }),
            trim: new THREE.MeshStandardMaterial({ color: 0x13161c, metalness: .4, roughness: .55 }),
            chrome: new THREE.MeshStandardMaterial({ color: 0xd8dee7, metalness: 1, roughness: .11, envMapIntensity: 2.1 }),
            chromeD: new THREE.MeshStandardMaterial({ color: 0x5f6a78, metalness: 1, roughness: .32, envMapIntensity: 1.5 }),
            alloy: new THREE.MeshStandardMaterial({ color: 0x9ba4b0, metalness: 1, roughness: .25, envMapIntensity: 1.7 }),
            alloyD: new THREE.MeshStandardMaterial({ color: 0x2f3742, metalness: .9, roughness: .5 }),
            disc: new THREE.MeshStandardMaterial({ color: 0x6f757e, metalness: 1, roughness: .45 }),
            caliper: new THREE.MeshStandardMaterial({ color: 0xB03A28, metalness: .4, roughness: .42 }),
            lensC: new THREE.MeshPhysicalMaterial({ color: 0xe4edf6, metalness: 0, roughness: .04, transparent: true, opacity: .38, envMapIntensity: 2.8, side: THREE.DoubleSide }),
            lensR: new THREE.MeshPhysicalMaterial({ color: 0x8d1410, metalness: 0, roughness: .09, transparent: true, opacity: .72, envMapIntensity: 2.2, side: THREE.DoubleSide }),
            lensA: new THREE.MeshPhysicalMaterial({ color: 0xb06510, metalness: 0, roughness: .09, transparent: true, opacity: .72, envMapIntensity: 2.2, side: THREE.DoubleSide }),
            reflect: new THREE.MeshStandardMaterial({ color: 0xcdd6e2, metalness: 1, roughness: .16, envMapIntensity: 1.9 }),
            bulb: new THREE.MeshStandardMaterial({ color: 0x0d0f13, emissive: 0xfff0d8, emissiveIntensity: .08, roughness: .3 }),
            carpet: new THREE.MeshStandardMaterial({ color: 0x14161b, metalness: .02, roughness: .97 }),
            leather: new THREE.MeshStandardMaterial({ color: 0x1b1d22, metalness: .04, roughness: .86 }),
            steel: new THREE.MeshStandardMaterial({ color: 0x4b525c, metalness: .9, roughness: .4 }),
            liner: new THREE.MeshStandardMaterial({ color: 0x0b0d10, metalness: .05, roughness: .98, side: THREE.DoubleSide }),
            slide: new THREE.MeshStandardMaterial({ color: 0x23272e, metalness: .55, roughness: .5 }),
            // Blueprint/isolate-mode materials — not wired to any toggle yet (that's the picker's
            // UI chrome, not this geometry pass), but kept 1:1 with the reference's own material
            // set rather than omitted.
            ghost: new THREE.MeshStandardMaterial({ color: 0x161b23, metalness: .15, roughness: .9, envMapIntensity: .25 }),
            ghostG: new THREE.MeshStandardMaterial({ color: 0x10151c, metalness: .15, roughness: .75, transparent: true, opacity: .55, envMapIntensity: .4 }),
            bluep: new THREE.MeshStandardMaterial({ color: 0x0c1826, metalness: .1, roughness: .9, envMapIntensity: .1 })
        };
        M.tyre = new THREE.MeshStandardMaterial({ color: 0x14171b, metalness: .03, roughness: .92, map: mkTread() });
        M.mesh = new THREE.MeshStandardMaterial({ color: 0x0a0c10, metalness: .6, roughness: .5, map: mkMesh() });
        return M;
    }

    // wingT1/qtrT1 taper the wing/quarter's outer edge down into the arch lip; archMaskF/R are
    // where that taper is allowed to happen (zero everywhere else, so the panel keeps its normal
    // edge outside the arch and only pulls in across the wheel opening).
    const wingT0 = 0.435;
    const archMaskF = x => smoothstep(1.01, 1.07, x) * smoothstep(1.81, 1.75, x);
    const archMaskR = x => smoothstep(-0.89, -0.95, x) * smoothstep(-1.69, -1.63, x);
    const wingT1 = f => 0.955 - 0.150 * archMaskF(1.847 - 0.821 * f);
    const qtrT1 = f => 0.955 - 0.150 * archMaskR(-0.886 - 1.064 * f);
    const qT0 = f => 0.390 + 0.058 * smoothstep(0.690, 0.810, f);

    function buildShell(M, car, B) {
        const shell = bp({ x0: 2.280, x1: -2.280, t0: -1, t1: 1, dx: 0.035, dt: 0.030, off: -0.009, mat: M.shell, shadow: true });
        shell.userData.noPick = true; car.add(shell);

        const tub = bp({ x0: 0.850, x1: -1.566, t0: -0.385, t1: 0.385, dx: 0.045, dt: 0.020, off: 0.002, mat: M.carpet });
        tub.userData.noPick = true; car.add(tub);

        // bonnet — narrow nose section between the lamps, then full width behind them
        B.Bonnet = [
            bp({ x0: 2.140, x1: 1.860, t0: -0.135, t1: 0.135, dx: 0.030, dt: 0.018, mat: M.paint }),
            bp({ x0: 1.860, x1: 1.246, t0: -0.415, t1: 0.415, dx: 0.034, dt: 0.018, mat: M.paint })
        ];

        B.CowlPanel = [bp({ x0: 1.239, x1: 1.106, t0: -0.415, t1: 0.415, dx: 0.020, dt: 0.018, mat: M.black })];

        const wings = bpPair({ x0: 1.847, x1: 1.026, t0: wingT0, t1: wingT1, dx: 0.026, dt: 0.020, mat: M.paint });
        B.WingFrontRight = [wings[0]];
        B.WingFrontLeft = [wings[1]];

        const doors = bpPair({ x0: 1.020, x1: 0.045, t0: 0.390, t1: 0.735, dx: 0.028, dt: 0.018, mat: M.paint });
        B.DoorFrontRight = [doors[0]];
        B.DoorFrontLeft = [doors[1]];
        const rdoors = bpPair({ x0: 0.039, x1: -0.880, t0: 0.390, t1: 0.735, dx: 0.028, dt: 0.018, mat: M.paint });
        B.DoorRearRight = [rdoors[0]];
        B.DoorRearLeft = [rdoors[1]];

        const sills = bpPair({ x0: 1.020, x1: -0.886, t0: 0.7395, t1: 0.955, dx: 0.040, dt: 0.020, mat: M.paint });
        B.SillRight = [sills[0]];
        B.SillLeft = [sills[1]];

        const quarters = bpPair({ x0: -0.886, x1: -1.950, t0: qT0, t1: qtrT1, dx: 0.026, dt: 0.020, mat: M.paint });
        B.QuarterRearRight = [quarters[0]];
        B.QuarterRearLeft = [quarters[1]];

        // Front wheel-house liners ARE clickable (FenderlinerFrontLeft/Right exist in Part.cs);
        // the rear equivalents are not — Part.cs only tracks front fenderliners, matching this
        // reference's own choice to leave its rear liners permanently userData.noPick.
        const liners = bpPair({ x0: 1.830, x1: 0.990, t0: 0.792, t1: 0.978, dx: 0.026, dt: 0.014, off: -0.004, mat: M.liner });
        B.FenderlinerFrontRight = [liners[0]];
        B.FenderlinerFrontLeft = [liners[1]];
        const rlin = new THREE.Group();
        bpPair({ x0: -0.875, x1: -1.710, t0: 0.792, t1: 0.978, dx: 0.026, dt: 0.014, off: -0.004, mat: M.liner })
            .forEach(m => { m.userData.noPick = true; rlin.add(m); });
        rlin.userData.noPick = true; car.add(rlin);

        B.BootTailgate = [bp({ x0: -1.566, x1: -2.3465, t0: -0.415, t1: 0.415, dx: 0.026, dt: 0.018, mat: M.paint })];

        B.FrontBumper = [
            capMesh(2.350, 0.005, M.paint),
            bp({ x0: 2.350, x1: 2.147, t0: -1, t1: 1, dx: 0.018, dt: 0.026, mat: M.paint }),
            bp({ x0: 2.140, x1: 1.869, t0: 0.435, t1: 1.0, dx: 0.026, dt: 0.022, mat: M.paint }),
            bp({ x0: 2.140, x1: 1.869, t0: -0.435, t1: -1.0, dx: 0.026, dt: 0.022, mat: M.paint })
        ];
        B.RearBumper = [
            capMesh(-2.350, -0.005, M.paint),
            bp({ x0: -1.944, x1: -2.350, t0: 0.481, t1: 1.0, dx: 0.026, dt: 0.022, mat: M.paint }),
            bp({ x0: -1.944, x1: -2.350, t0: -0.481, t1: -1.0, dx: 0.026, dt: 0.022, mat: M.paint })
        ];

        const fSlide = bpPair({ x0: 1.874, x1: 1.826, t0: 0.430, t1: 0.930, dx: 0.010, dt: 0.018, off: -0.004, mat: M.slide });
        B.BumperSlideFrontRight = [fSlide[0]];
        B.BumperSlideFrontLeft = [fSlide[1]];
        const rSlide = bpPair({ x0: -1.922, x1: -1.980, t0: 0.430, t1: 0.930, dx: 0.010, dt: 0.018, off: -0.004, mat: M.slide });
        B.BumperSlideRearRight = [rSlide[0]];
        B.BumperSlideRearLeft = [rSlide[1]];
    }

    function buildGreenhouse(M, car, B) {
        B.Windscreen = [gp({ x0: WS_X0, x1: WS_X1, t0: f => -uWi(f) - 0.02, t1: f => uWi(f) + 0.02, dx: 0.026, dt: 0.016, off: -0.004, mat: M.glass, shadow: false })];

        const pillA = gpPair({ x0: WS_X0, x1: WS_X1, t0: uWi, t1: uPo, dx: 0.024, dt: 0.012, off: 0.004, mat: M.paint });
        B.PillarARight = [pillA[0]];
        B.PillarALeft = [pillA[1]];

        B.Roof = [gp({ x0: WS_X1, x1: RF_X1, t0: f => -EDGE_IN(WS_X1 + (RF_X1 - WS_X1) * f) - 0.01, t1: f => EDGE_IN(WS_X1 + (RF_X1 - WS_X1) * f) + 0.01, dx: 0.030, dt: 0.020, off: 0.004, mat: M.paint })];

        const rails = gpPair({ x0: WS_X1, x1: -1.030, t0: edgeIn(WS_X1, -1.030), t1: edgeOut(WS_X1, -1.030), dx: 0.030, dt: 0.010, off: 0.006, mat: M.trim });
        B.DripRailRight = [rails[0]];
        B.DripRailLeft = [rails[1]];

        const doB1 = { x0: WS_X0, x1: WS_X1 }, doB2 = { x0: WS_X1, x1: 0.042 };
        B.DoorGlassFrontRight = [
            gp({ x0: doB1.x0, x1: doB1.x1, t0: f => edgeOut(doB1.x0, doB1.x1)(f) - 0.02, t1: 1.0, dx: 0.026, dt: 0.014, off: -0.004, mat: M.glassD, shadow: false }),
            gp({ x0: doB2.x0, x1: doB2.x1, t0: f => edgeOut(doB2.x0, doB2.x1)(f) - 0.02, t1: 1.0, dx: 0.030, dt: 0.014, off: -0.004, mat: M.glassD, shadow: false })
        ];
        B.DoorGlassFrontLeft = [
            gp({ x0: doB1.x0, x1: doB1.x1, t0: f => -edgeOut(doB1.x0, doB1.x1)(f) + 0.02, t1: -1.0, dx: 0.026, dt: 0.014, off: -0.004, mat: M.glassD, shadow: false }),
            gp({ x0: doB2.x0, x1: doB2.x1, t0: f => -edgeOut(doB2.x0, doB2.x1)(f) + 0.02, t1: -1.0, dx: 0.030, dt: 0.014, off: -0.004, mat: M.glassD, shadow: false })
        ];

        const rgl = gpPair({ x0: -0.016, x1: -0.874, t0: f => edgeOut(-0.016, -0.874)(f) - 0.02, t1: 1.0, dx: 0.028, dt: 0.014, off: -0.004, mat: M.glassD, shadow: false });
        B.DoorGlassRearRight = [rgl[0]];
        B.DoorGlassRearLeft = [rgl[1]];

        const bpill = gpPair({ x0: 0.042, x1: -0.016, t0: edgeIn(0.042, -0.016), t1: 1.0, dx: 0.014, dt: 0.014, off: 0.005, mat: M.black });
        B.PillarBRight = [bpill[0]];
        B.PillarBLeft = [bpill[1]];

        const qglass = gpPair({ x0: -0.880, x1: -1.030, t0: f => edgeOut(-0.880, -1.030)(f) - 0.02, t1: 1.0, dx: 0.022, dt: 0.014, off: -0.004, mat: M.glassD, shadow: false });
        B.QuarterGlassRight = [qglass[0]];
        B.QuarterGlassLeft = [qglass[1]];

        const cpill = gpPair({ x0: -1.030, x1: BL_X1, t0: edgeIn(-1.030, BL_X1), t1: 1.0, dx: 0.026, dt: 0.016, off: 0.004, mat: M.paint });
        B.PillarCRight = [cpill[0]];
        B.PillarCLeft = [cpill[1]];

        B.RearWindscreen = [gp({ x0: RF_X1, x1: BL_X1, t0: f => -edgeIn(RF_X1, BL_X1)(f) - 0.02, t1: f => edgeIn(RF_X1, BL_X1)(f) + 0.02, dx: 0.026, dt: 0.016, off: -0.004, mat: M.glass, shadow: false })];

        // Beltline weatherstrip (rubber seal the drop glass wipes against) and the ceramic frit
        // border around the bonded glass — both pure decoration, never pickable.
        const beltSeal = new THREE.Group();
        bpPair({ x0: 1.095, x1: -1.590, t0: 0.370, t1: 0.398, dx: 0.040, dt: 0.009, off: 0.005, mat: M.rubber })
            .forEach(m => { m.userData.noPick = true; beltSeal.add(m); });
        beltSeal.userData.noPick = true; car.add(beltSeal);

        const frit = new THREE.Group();
        frit.add(gp({ x0: WS_X0, x1: WS_X0 - 0.045, t0: f => -uWi(f), t1: f => uWi(f), dx: 0.010, dt: 0.020, off: 0.0, mat: M.black, shadow: false }));
        frit.add(gp({ x0: WS_X0, x1: WS_X1, t0: f => uWi(f) - 0.045, t1: uWi, dx: 0.026, dt: 0.010, off: 0.0, mat: M.black, shadow: false }));
        frit.add(gp({ x0: WS_X0, x1: WS_X1, t0: f => -uWi(f) + 0.045, t1: f => -uWi(f), dx: 0.026, dt: 0.010, off: 0.0, mat: M.black, shadow: false }));
        const edgeBL = edgeIn(RF_X1, BL_X1);
        frit.add(gp({ x0: BL_X1, x1: -1.455, t0: f => -edgeBL(1), t1: f => edgeBL(1), dx: 0.010, dt: 0.020, off: 0.0, mat: M.black, shadow: false }));
        frit.add(gp({ x0: RF_X1, x1: BL_X1, t0: f => edgeBL(f) - 0.045, t1: edgeBL, dx: 0.026, dt: 0.010, off: 0.0, mat: M.black, shadow: false }));
        frit.add(gp({ x0: RF_X1, x1: BL_X1, t0: f => -edgeBL(f) + 0.045, t1: f => -edgeBL(f), dx: 0.026, dt: 0.010, off: 0.0, mat: M.black, shadow: false }));
        frit.userData.noPick = true; car.add(frit);
    }

    // ---------- lamps, grilles, apertures, spoilers, wheels ----------
    function facePlate(x, y, z, h, w, d, mat) {
        const m = new THREE.Mesh(new THREE.BoxGeometry(d, h, w), mat);
        m.position.set(x, y, z); m.castShadow = true; m.receiveShadow = true; return m;
    }
    function faceFrame(x, y, z, h, w, d, th, mat) {
        const g = new THREE.Group();
        g.add(facePlate(x, y + h / 2 + th / 2, z, th, w + 2 * th, d, mat));
        g.add(facePlate(x, y - h / 2 - th / 2, z, th, w + 2 * th, d, mat));
        g.add(facePlate(x, y, z + w / 2 + th / 2, h, th, d, mat));
        g.add(facePlate(x, y, z - w / 2 - th / 2, h, th, d, mat));
        return g;
    }
    function bezel(x0, x1, t0, t1, mat, off) {
        const w = 0.012;
        return [
            bp({ x0: x0, x1: x1, t0: t0 - w, t1: t0, dx: 0.030, dt: 0.006, off: off, mat: mat }),
            bp({ x0: x0, x1: x1, t0: t1, t1: t1 + w, dx: 0.030, dt: 0.006, off: off, mat: mat }),
            bp({ x0: x0, x1: x0 - 0.014, t0: t0 - w, t1: t1 + w, dx: 0.007, dt: 0.012, off: off, mat: mat }),
            bp({ x0: x1, x1: x1 + 0.014, t0: t0 - w, t1: t1 + w, dx: 0.007, dt: 0.012, off: off, mat: mat })
        ];
    }

    function buildLampsGrillesWheelsSpoilers(M, car, B) {
        function headlamp(sgn) {
            const t0 = sgn * 0.150, t1 = sgn * 0.408, x0 = 2.058, x1 = 1.812;
            const parts = [
                bp({ x0: x0, x1: x1, t0: t0, t1: t1, dx: 0.024, dt: 0.016, off: -0.030, mat: M.reflect }),
                bp({ x0: x0, x1: x1, t0: t0, t1: t1, dx: 0.024, dt: 0.016, off: 0.004, mat: M.lensC, shadow: false }),
                ...bezel(x0, x1, Math.min(t0, t1), Math.max(t0, t1), M.black, 0.006)
            ];
            const bowl = new THREE.Mesh(new THREE.SphereGeometry(0.052, 20, 12, 0, Math.PI * 2, 0, Math.PI * 0.55), M.reflect);
            bowl.rotation.x = Math.PI;
            const bg = new THREE.Group(); bg.add(bowl);
            bowl.position.z = -0.055;
            const lens2 = new THREE.Mesh(new THREE.SphereGeometry(0.035, 18, 10), M.lensC);
            lens2.position.z = -0.02; lens2.scale.z = 0.45; bg.add(lens2);
            place(bg, 1.985, sgn * 0.245, -0.012);
            parts.push(bg);
            const amber = bp({ x0: 1.870, x1: 1.812, t0: sgn * 0.150, t1: sgn * 0.408, dx: 0.020, dt: 0.016, off: 0.005, mat: M.lensA, shadow: false });
            parts.push(amber);
            return parts;
        }
        B.HeadlightRight = headlamp(1);
        B.HeadlightLeft = headlamp(-1);

        const intakeMesh = M.mesh;
        B.MainGrill = [
            bp({ x0: 2.308, x1: 2.158, t0: -0.235, t1: 0.235, dx: 0.020, dt: 0.016, off: -0.034, mat: M.black }),
            bp({ x0: 2.302, x1: 2.094, t0: -0.230, t1: 0.230, dx: 0.020, dt: 0.016, off: -0.022, mat: intakeMesh, shadow: false }),
            ...bezel(2.238, 2.088, -0.235, 0.235, M.trim, 0.004)
        ];

        B.CentreGrill = [
            facePlate(2.347, 0.432, 0, 0.086, 0.400, 0.016, M.black),
            facePlate(2.353, 0.432, 0, 0.074, 0.388, 0.014, intakeMesh),
            faceFrame(2.357, 0.432, 0, 0.086, 0.400, 0.030, 0.013, M.trim)
        ];

        function bumperGrill(sgn) {
            const x0 = 2.235, x1 = 2.150, t0 = sgn * 0.440, t1 = sgn * 0.560;
            const a = Math.min(t0, t1), b = Math.max(t0, t1);
            return [
                bp({ x0: x0, x1: x1, t0: t0, t1: t1, dx: 0.014, dt: 0.012, off: -0.026, mat: M.black }),
                bp({ x0: x0 - 0.004, x1: x1 + 0.004, t0: t0, t1: t1, dx: 0.014, dt: 0.012, off: -0.014, mat: intakeMesh, shadow: false }),
                ...bezel(x0, x1, a, b, M.trim, 0.004)
            ];
        }
        B.BumperGrillFrontRight = bumperGrill(1);
        B.BumperGrillFrontLeft = bumperGrill(-1);

        function spot(sgn) {
            const g = new THREE.Group();
            const hous = new THREE.Mesh(new THREE.CylinderGeometry(0.044, 0.038, 0.048, 22), M.black);
            hous.rotation.x = Math.PI / 2; hous.position.z = -0.012; g.add(hous);
            const refl = new THREE.Mesh(new THREE.CylinderGeometry(0.040, 0.018, 0.036, 22), M.reflect);
            refl.rotation.x = -Math.PI / 2; refl.position.z = 0.004; g.add(refl);
            const bulb = new THREE.Mesh(new THREE.SphereGeometry(0.011, 12, 8), M.bulb); g.add(bulb);
            const lens = new THREE.Mesh(new THREE.CylinderGeometry(0.041, 0.041, 0.009, 22), M.lensC);
            lens.rotation.x = Math.PI / 2; lens.position.z = 0.018; g.add(lens);
            g.traverse(o => { if (o.isMesh) o.castShadow = true; });
            return place(g, 2.252, sgn * 0.640, 0.004);
        }
        B.SpotlampRight = [spot(1)];
        B.SpotlampLeft = [spot(-1)];

        function spotGrill(sgn) {
            const g = new THREE.Group();
            const disc = new THREE.Mesh(new THREE.CircleGeometry(0.040, 26), intakeMesh); g.add(disc);
            const ring = new THREE.Mesh(new THREE.TorusGeometry(0.043, 0.006, 10, 30), M.trim); g.add(ring);
            for (let i = 0; i < 2; i++) {
                const bar = new THREE.Mesh(new THREE.BoxGeometry(0.080, 0.007, 0.008), M.trim);
                bar.position.y = (i ? 0.015 : -0.015); g.add(bar);
            }
            g.traverse(o => { if (o.isMesh) o.castShadow = true; });
            return place(g, 2.252, sgn * 0.640, 0.026);
        }
        B.SpotlampGrillRight = [spotGrill(1)];
        B.SpotlampGrillLeft = [spotGrill(-1)];

        function taillamp(sgn) {
            const y = 0.700, h = 0.160, w = 0.372, zc = sgn * 0.297;
            return [
                facePlate(-2.348, y, zc, h, w, 0.036, M.reflect),
                facePlate(-2.358, y, zc, h - 0.014, w - 0.014, 0.016, M.lensR),
                facePlate(-2.361, y, zc + sgn * 0.104, h - 0.036, 0.092, 0.014, M.lensA),
                facePlate(-2.361, y, zc - sgn * 0.136, h - 0.052, 0.062, 0.014, M.lensC),
                faceFrame(-2.354, y, zc, h, w, 0.028, 0.013, M.black),
                bp({ x0: -2.338, x1: -2.150, t0: sgn * 0.492, t1: sgn * 0.578, dx: 0.013, dt: 0.009, off: -0.006, mat: M.reflect }),
                bp({ x0: -2.338, x1: -2.222, t0: sgn * 0.496, t1: sgn * 0.574, dx: 0.013, dt: 0.009, off: 0.005, mat: M.lensR, shadow: false }),
                ...bezel(-2.150, -2.338, Math.min(sgn * 0.492, sgn * 0.578), Math.max(sgn * 0.492, sgn * 0.578), M.black, 0.007)
            ];
        }
        B.TaillightRight = taillamp(1);
        B.TaillightLeft = taillamp(-1);

        // Centre garnish and rear valance/diffuser have no Part.cs equivalent — visual
        // completeness only, same treatment as the reference's own non-pickable trim.
        B.CentreGarnish = [
            facePlate(-2.350, 0.700, 0, 0.170, 0.230, 0.024, M.black),
            faceFrame(-2.354, 0.700, 0, 0.170, 0.230, 0.026, 0.012, M.trim)
        ];

        B.FrontSpoiler = [
            bp({
                x0: 2.328, x1: 2.090, t0: 0.836, t1: 1.164, dx: 0.014, dt: 0.016,
                off: (fx, ft) => 0.006 + 0.030 * Math.pow(1 - fx, 1.4) * (1 - 0.30 * Math.pow(Math.abs(ft * 2 - 1), 3)), mat: M.black
            }),
            bp({ x0: 2.328, x1: 2.302, t0: 0.836, t1: 1.164, dx: 0.009, dt: 0.016, off: 0.034, mat: M.black })
        ];

        B.RearSpoiler = [
            bp({
                x0: -2.100, x1: -2.266, t0: -0.395, t1: 0.395, dx: 0.014, dt: 0.018,
                off: (fx, ft) => 0.004 + 0.055 * Math.pow(fx, 1.6) * (1 - 0.22 * Math.pow(Math.abs(ft * 2 - 1), 3)), mat: M.paint
            }),
            bp({ x0: -2.266, x1: -2.284, t0: -0.395, t1: 0.395, dx: 0.009, dt: 0.018, off: 0.040, mat: M.paint }),
            bp({
                x0: -2.284, x1: -2.308, t0: -0.395, t1: 0.395, dx: 0.008, dt: 0.018,
                off: (fx) => 0.040 * (1 - fx) + 0.003 * fx, mat: M.paint
            })
        ];

        B.RearValance = [
            bp({ x0: -2.346, x1: -2.210, t0: 0.780, t1: 1.220, dx: 0.020, dt: 0.020, off: -0.020, mat: M.black }),
            ...bezel(-2.346, -2.210, 0.780, 1.220, M.trim, 0.003)
        ];

        // ---------- wheels & brakes ----------
        function makeWheel() {
            const g = new THREE.Group();
            const RIM = 0.2032, HW = 0.1025;
            const pts = [
                [RIM, -HW], [RIM + 0.014, -HW - 0.007], [0.246, -HW - 0.010], [0.288, -0.099], [0.307, -0.083],
                [0.3145, -0.062], [0.3160, -0.030], [0.3160, 0.030], [0.3145, 0.062], [0.307, 0.083],
                [0.288, 0.099], [0.246, HW + 0.010], [RIM + 0.014, HW + 0.007], [RIM, HW]
            ].map(p => new THREE.Vector2(p[0], p[1]));
            const tyre = new THREE.Mesh(new THREE.LatheGeometry(pts, 60), M.tyre);
            tyre.rotation.x = Math.PI / 2; tyre.castShadow = true; tyre.receiveShadow = true; g.add(tyre);
            const band = new THREE.Mesh(new THREE.TorusGeometry(0.268, 0.008, 8, 52), M.rubber);
            band.position.z = HW - 0.004; g.add(band);
            const band2 = band.clone(); band2.position.z = -HW + 0.004; g.add(band2);

            const barrel = new THREE.Mesh(new THREE.LatheGeometry([
                [0.196, -HW - 0.004], [RIM, -HW], [0.196, -0.055], [0.188, 0.010], [0.196, 0.062], [RIM, HW], [0.196, HW + 0.004]
            ].map(p => new THREE.Vector2(p[0], p[1])), 48), M.alloyD);
            barrel.rotation.x = Math.PI / 2; g.add(barrel);

            const lip = new THREE.Mesh(new THREE.TorusGeometry(0.1985, 0.011, 10, 52), M.alloy);
            lip.position.z = HW - 0.012; lip.castShadow = true; g.add(lip);
            const innerMat = new THREE.MeshStandardMaterial({ color: 0x2f3742, metalness: .9, roughness: .5, side: THREE.DoubleSide });
            const inner = new THREE.Mesh(new THREE.CircleGeometry(0.199, 44), innerMat);
            inner.position.z = -0.030; inner.rotation.y = Math.PI; g.add(inner);

            const hub = new THREE.Mesh(new THREE.CylinderGeometry(0.062, 0.070, 0.062, 26), M.alloy);
            hub.rotation.x = Math.PI / 2; hub.position.z = HW - 0.052; g.add(hub);
            const capC = new THREE.Mesh(new THREE.CylinderGeometry(0.036, 0.036, 0.010, 22), M.alloyD);
            capC.rotation.x = Math.PI / 2; capC.position.z = HW - 0.020; g.add(capC);

            for (let i = 0; i < 5; i++) {
                const a = i * Math.PI * 2 / 5;
                for (const off of [-0.115, 0.115]) {
                    const sh = new THREE.Shape();
                    sh.moveTo(-0.020, 0.052); sh.lineTo(0.020, 0.052);
                    sh.lineTo(0.038, 0.196); sh.lineTo(-0.028, 0.196); sh.closePath();
                    const sp = new THREE.Mesh(new THREE.ExtrudeGeometry(sh, { depth: 0.020, bevelEnabled: true, bevelSize: 0.004, bevelThickness: 0.004, bevelSegments: 1 }), M.alloy);
                    sp.position.z = HW - 0.048;
                    sp.rotation.z = a + off * 0.34;
                    sp.castShadow = true;
                    g.add(sp);
                }
                const lug = new THREE.Mesh(new THREE.CylinderGeometry(0.010, 0.010, 0.014, 6), M.chromeD);
                lug.rotation.x = Math.PI / 2;
                lug.position.set(Math.cos(a + 0.3) * 0.046, Math.sin(a + 0.3) * 0.046, HW - 0.026);
                g.add(lug);
            }
            return g;
        }
        function makeBrake(r) {
            const g = new THREE.Group();
            const disc = new THREE.Mesh(new THREE.CylinderGeometry(r, r, 0.024, 44), M.disc);
            disc.rotation.x = Math.PI / 2; disc.position.z = 0.012; g.add(disc);
            const hat = new THREE.Mesh(new THREE.CylinderGeometry(0.078, 0.084, 0.052, 26), M.alloyD);
            hat.rotation.x = Math.PI / 2; hat.position.z = 0.044; g.add(hat);
            const cal = new THREE.Mesh(new THREE.BoxGeometry(0.062, 0.148, 0.070), M.caliper);
            cal.position.set(-r * 0.68, r * 0.62, 0.012); cal.rotation.z = -0.72; g.add(cal);
            g.traverse(o => { if (o.isMesh) o.castShadow = true; });
            return g;
        }
        const TRACK_F = 0.755, TRACK_R = 0.750;
        function corner(x, z, sgn, r) {
            const g = new THREE.Group();
            const w = makeWheel(); const b = makeBrake(r);
            g.add(w, b);
            g.position.set(x, WHEEL_Y, z);
            const k = 1.048; // 315mm -> 330mm rolling radius, 205 -> 215 section
            g.scale.set(k, k, sgn < 0 ? -k : k);
            return g;
        }
        B.WheelFrontRight = [corner(AXLE_F, TRACK_F, 1, 0.140)];
        B.WheelFrontLeft = [corner(AXLE_F, -TRACK_F, -1, 0.140)];
        B.WheelRearRight = [corner(AXLE_R, TRACK_R, 1, 0.133)];
        B.WheelRearLeft = [corner(AXLE_R, -TRACK_R, -1, 0.133)];
    }

    function mkPlateTexture(txt) {
        const c = document.createElement('canvas'); c.width = 512; c.height = 128;
        const g = c.getContext('2d');
        g.fillStyle = '#e8e6df'; g.fillRect(0, 0, 512, 128);
        g.strokeStyle = '#1a1d24'; g.lineWidth = 6; g.strokeRect(10, 10, 492, 108);
        g.fillStyle = '#1a1d24'; g.font = '600 68px monospace';
        g.textAlign = 'center'; g.textBaseline = 'middle'; g.fillText(txt, 256, 68);
        const t = new THREE.CanvasTexture(c); t.encoding = THREE.sRGBEncoding; return t;
    }

    // Mirrors ARE clickable (MirrorLeft/Right exist in Part.cs); handles, wipers, plates,
    // exhaust, the antenna and the fuel filler are not — visual completeness only, same
    // treatment as the reference's own non-pickable trim.
    function buildFurniture(M, car, B) {
        function mirror(sgn) {
            const g = new THREE.Group();
            const shellM = new THREE.Mesh(new THREE.SphereGeometry(0.072, 20, 14, 0, Math.PI * 2, 0, Math.PI * 0.62), M.paint);
            shellM.rotation.x = -Math.PI / 2; shellM.scale.set(1, 0.62, 0.78);
            shellM.position.set(0, 0, 0.03); g.add(shellM);
            const glass = new THREE.Mesh(new THREE.CircleGeometry(0.055, 20), M.chrome);
            glass.scale.set(1, 0.72, 1); glass.position.set(0, 0, 0.032); glass.rotation.y = Math.PI; g.add(glass);
            const stalk = new THREE.Mesh(new THREE.BoxGeometry(0.048, 0.042, 0.055), M.black);
            stalk.position.set(0, -0.03, -0.03); g.add(stalk);
            const back = new THREE.Mesh(new THREE.SphereGeometry(0.071, 18, 12, 0, Math.PI * 2, 0, Math.PI * 0.62), M.paint);
            back.rotation.x = Math.PI / 2; back.scale.set(1, 0.62, 0.78); back.position.z = 0.028; g.add(back);
            place(g, 1.010, sgn * 0.500, 0.030);
            g.children.forEach(c2 => { c2.castShadow = true; });
            return g;
        }
        B.MirrorRight = [mirror(1)];
        B.MirrorLeft = [mirror(-1)];

        function handle(sgn, hx) {
            const g = new THREE.Group();
            const cup = new THREE.Mesh(new THREE.BoxGeometry(0.135, 0.042, 0.026), M.black);
            cup.position.z = -0.008; g.add(cup);
            const bar = new THREE.Mesh(new THREE.BoxGeometry(0.115, 0.024, 0.03), M.paint);
            bar.position.z = 0.012; g.add(bar);
            return place(g, hx, sgn * 0.470, 0.010);
        }
        B.DoorHandleFrontRight = [handle(1, 0.190)];
        B.DoorHandleFrontLeft = [handle(-1, 0.190)];
        B.DoorHandleRearRight = [handle(1, -0.710)];
        B.DoorHandleRearLeft = [handle(-1, -0.710)];

        function repeater(sgn) {
            const g = new THREE.Group();
            const l = new THREE.Mesh(new THREE.SphereGeometry(0.030, 14, 10), M.lensA);
            l.scale.set(1, 0.62, 0.42); g.add(l);
            return place(g, 1.060, sgn * 0.520, 0.004);
        }
        B.SideRepeaterRight = [repeater(1)];
        B.SideRepeaterLeft = [repeater(-1)];

        function wiper(zc, len, ang) {
            const g = new THREE.Group();
            const arm = new THREE.Mesh(new THREE.BoxGeometry(len, 0.014, 0.010), M.black);
            arm.position.x = len / 2; g.add(arm);
            const blade = new THREE.Mesh(new THREE.BoxGeometry(len * 0.78, 0.020, 0.006), M.rubber);
            blade.position.set(len * 0.55, -0.012, 0); g.add(blade);
            const piv = new THREE.Mesh(new THREE.CylinderGeometry(0.016, 0.016, 0.022, 14), M.chromeD);
            piv.rotation.x = Math.PI / 2; g.add(piv);
            g.rotation.z = ang;
            const holder = new THREE.Group(); holder.add(g);
            return place(holder, 1.160, zc, 0.012);
        }
        // One part covering both blades, same as the reference's own single 'Wipers' entry —
        // there is no honest way to tag "just the passenger-side wiper" on a real photo either.
        B.Wipers = [wiper(0.34, 0.52, 0.34), wiper(-0.12, 0.50, 0.30)];

        function plate(x, y, txt, face) {
            const g = new THREE.Group();
            const faceMat = new THREE.MeshStandardMaterial({ map: mkPlateTexture(txt), roughness: .5, metalness: .05 });
            const side = M.black;
            const m = new THREE.Mesh(new THREE.BoxGeometry(0.012, 0.096, 0.33),
                face > 0 ? [faceMat, side, side, side, side, side] : [side, faceMat, side, side, side, side]);
            m.castShadow = true; g.add(m);
            const pad = new THREE.Mesh(new THREE.BoxGeometry(0.010, 0.112, 0.35), M.black);
            pad.position.x = -0.008 * face; g.add(pad);
            g.position.set(x, y, 0);
            return g;
        }
        B.FrontPlate = [plate(2.360, 0.530, 'CA 4560', 1)];
        B.RearPlate = [plate(-2.360, 0.490, 'CA 4560', -1)];

        const exhMeshes = [];
        [-0.300, -0.432].forEach(z => {
            const pipe = new THREE.Mesh(new THREE.CylinderGeometry(0.041, 0.041, 0.20, 20), M.chromeD);
            pipe.position.set(-2.16, 0.288, z); pipe.rotation.z = Math.PI / 2;
            const tip = new THREE.Mesh(new THREE.CylinderGeometry(0.046, 0.043, 0.05, 20), M.chrome);
            tip.position.set(-2.248, 0.288, z); tip.rotation.z = Math.PI / 2;
            const bore = new THREE.Mesh(new THREE.CylinderGeometry(0.038, 0.038, 0.03, 20), M.black);
            bore.position.set(-2.264, 0.288, z); bore.rotation.z = Math.PI / 2;
            [pipe, tip, bore].forEach(m => { m.castShadow = true; exhMeshes.push(m); });
        });
        B.ExhaustTips = exhMeshes;

        const antMeshes = [];
        (function () {
            const base = pointAt(-1.180, 0.760, new THREE.Vector3());
            const n = normalAt(-1.180, 0.760, new THREE.Vector3());
            const ant = new THREE.Group();
            const foot = new THREE.Mesh(new THREE.CylinderGeometry(0.011, 0.015, 0.026, 12), M.black);
            foot.position.set(0, 0.010, 0); foot.rotation.z = -0.30; ant.add(foot);
            const mast = new THREE.Mesh(new THREE.CylinderGeometry(0.0035, 0.0055, 0.335, 10), M.chromeD);
            mast.position.set(0.048, 0.175, 0); mast.rotation.z = -0.30; ant.add(mast);
            ant.position.copy(base).addScaledVector(n, 0.006);
            antMeshes.push(ant);
        })();
        B.Antenna = antMeshes;

        const fuel = new THREE.Group();
        const cap = new THREE.Mesh(new THREE.CylinderGeometry(0.075, 0.075, 0.008, 26), M.paint);
        cap.rotation.x = Math.PI / 2; fuel.add(cap);
        const ring = new THREE.Mesh(new THREE.TorusGeometry(0.076, 0.004, 8, 26), M.black);
        fuel.add(ring);
        place(fuel, -1.680, -0.505, 0.006);
        B.FuelFiller = [fuel];
    }

    function build() {
        const car = new THREE.Group();
        const M = buildMaterials();
        const B = {}; // Part enum name -> array of meshes

        buildShell(M, car, B);
        buildGreenhouse(M, car, B);
        buildLampsGrillesWheelsSpoilers(M, car, B);
        buildFurniture(M, car, B);

        const partMeshes = {};
        Object.keys(B).forEach(partName => {
            const group = new THREE.Group();
            group.name = partName;
            // The raycast in car3d.js's pick() only ever hits a leaf mesh, then walks UP looking
            // for this flag to find the part it belongs to — required because a part can be
            // several meshes (FrontBumper is a nose cap plus three body patches) sharing one
            // click target.
            group.userData.isPart = true;
            B[partName].forEach(m => group.add(m));
            car.add(group);
            partMeshes[partName] = group;
        });

        return { group: car, partMeshes: partMeshes, materials: M };
    }

    global.CarBody = { build: build };
})(window);
