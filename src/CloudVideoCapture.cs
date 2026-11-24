namespace TideCombat
{
    internal static class CloudVideoCapture
    {
        // The page supplies its decoded video track. The processor keeps only its
        // newest frame, and expires if WW stops reading even after a lost CDP session.
        internal const string ReadVideo = @"
            (el, x, y, width, height) => {
                const stateKey = '__tideCombatVideo';
                const receivers = readTracks => {
                    const rtc = window.RTCPeerConnection && window.RTCPeerConnection.prototype;
                    const key = Symbol.for('tideCombat.receiverSources');
                    if (!rtc || !window.WeakRef) return [];
                    let peers = rtc[key];
                    if (!peers) {
                        peers = [];
                        const original = rtc.setRemoteDescription;
                        if (typeof original !== 'function') return [];
                        rtc.setRemoteDescription = function(...args) {
                            for (let i = peers.length - 1; i >= 0; --i) {
                                const peer = peers[i].deref();
                                if (!peer || peer === this) peers.splice(i, 1);
                            }
                            if (peers.length >= 16) peers.shift();
                            peers.push(new WeakRef(this));
                            return Reflect.apply(original, this, args);
                        };
                        Object.defineProperty(rtc, key, { value: peers });
                    }
                    if (!readTracks) return [];
                    const tracks = new Set();
                    for (const ref of peers) {
                        const pc = ref.deref();
                        if (!pc || pc.connectionState !== 'connected') continue;
                        for (const receiver of pc.getReceivers()) {
                            const track = receiver.track;
                            if (track && track.kind === 'video' && track.readyState === 'live' && !track.muted)
                                tracks.add(track);
                        }
                    }
                    return Array.from(tracks);
                };
                const renderer = el instanceof HTMLCanvasElement;
                const received = receivers(renderer);
                const clear = () => {
                    const active = window[stateKey];
                    if (!active) return;
                    window[stateKey] = null;
                    clearTimeout(active.lease);
                    if (active.frame) active.frame.close();
                    active.frame = null;
                    active.reader.cancel().catch(() => {});
                    active.ownedTrack.stop();
                };
                const video = el instanceof HTMLVideoElement;
                if ((!video && !renderer) || !window.MediaStreamTrackProcessor || width > 1920 || height > 1080) {
                    clear();
                    return null;
                }
                if (video && (!el.srcObject || typeof el.srcObject.getVideoTracks !== 'function' || !el.videoWidth ||
                    el.paused || el.ended || el.seeking)) {
                    clear();
                    return null;
                }
                const tracks = video ? el.srcObject.getVideoTracks().filter(track => track.readyState === 'live') : received;
                if (tracks.length !== 1) {
                    clear();
                    return null;
                }
                const rect = el.getBoundingClientRect(), style = getComputedStyle(el);
                const ratio = video ? el.videoWidth / el.videoHeight : el.width / el.height;
                const centeredContain = style.objectFit === 'contain' && style.objectPosition === '50% 50%';
                if (rect.left < -1 || rect.top < -1 || rect.right > innerWidth + 1 || rect.bottom > innerHeight + 1 ||
                    !Number.isFinite(ratio) || Math.abs(width / height - ratio) > .01 ||
                    Math.abs(rect.width / rect.height - ratio) > .01 && !centeredContain ||
                    style.transform !== 'none' ||
                    [style.borderTopWidth, style.borderRightWidth, style.borderBottomWidth, style.borderLeftWidth,
                     style.paddingTop, style.paddingRight, style.paddingBottom, style.paddingLeft].some(v => parseFloat(v) > 0)) {
                    clear();
                    return null;
                }
                for (const [u,v] of [[.075,.075],[.925,.075],[.5,.5],[.075,.925],[.925,.925]]) {
                    if (document.elementFromPoint(x + width * u, y + height * v) !== el) return null;
                }
                const track = tracks[0];
                let active = window[stateKey];
                if (active && (active.element !== el || active.track !== track)) {
                    clear();
                    active = null;
                }
                if (!active) {
                    const canvas = document.createElement('canvas');
                    const ownedTrack = track.clone();
                    let reader;
                    try {
                        const processor = new MediaStreamTrackProcessor({ track: ownedTrack, maxBufferSize: 1 });
                        reader = processor.readable.getReader();
                    } catch (_) {
                        ownedTrack.stop();
                        return null;
                    }
                    active = { element: el, track, ownedTrack, canvas, reader, frame: null, received: 0,
                               used: false, lease: 0 };
                    const current = active;
                    window[stateKey] = active;
                    (async () => {
                        try {
                            while (window[stateKey] === current) {
                                const next = await current.reader.read();
                                if (next.done) break;
                                if (window[stateKey] !== current) {
                                    next.value.close();
                                    break;
                                }
                                if (current.frame) current.frame.close();
                                current.frame = next.value;
                                current.received = performance.now();
                                current.used = false;
                            }
                        } catch (_) { }
                        if (window[stateKey] === current) clear();
                    })();
                }
                clearTimeout(active.lease);
                active.lease = setTimeout(clear, 2000);
                if (!active.frame || active.used || performance.now() - active.received > 350 ||
                    Math.abs(active.frame.displayWidth / active.frame.displayHeight - width / height) > .01)
                    return null;
                active.used = true;
                const canvas = active.canvas;
                if (canvas.width !== width) canvas.width = width;
                if (canvas.height !== height) canvas.height = height;
                const context = canvas.getContext('2d', { alpha: false });
                if (!context) return null;
                context.drawImage(active.frame, 0, 0, width, height);
                return canvas.toDataURL('image/jpeg', .90).split(',')[1];
            }";
    }
}
