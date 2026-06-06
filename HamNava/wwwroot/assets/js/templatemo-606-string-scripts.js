/* JavaScript Document

TemplateMo 606 String Master
Customized for Ham Nava

*/

// Guitar configuration
const STRINGS = 6;
const FRETS = 13;
const STRING_NOTES = ["E4", "B3", "G3", "D3", "A2", "E2"];
const NOTE_NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
const NOTE_NAMES_FA = ["دو", "دو دیز", "ر", "ر دیز", "می", "فا", "فا دیز", "سل", "سل دیز", "لا", "لا دیز", "سی"];
const FLAT_TO_SHARP = {
   Db: "C#",
   Eb: "D#",
   Gb: "F#",
   Ab: "G#",
   Bb: "A#"
};

const formatToman = (value) => new Intl.NumberFormat("fa-IR").format(value);

// Symbolic note map for arranged melody playback on the current fretboard
const NOTE_SYMBOL_TO_POSITION = {
   A: [0, 5],
   B: [0, 7],
   C: [1, 1],
   D: [1, 3],
   E: [0, 0],
   F: [0, 1],
   "F`": [0, 1],
   G: [0, 3],
   "G#": [0, 4],
   "C'": [0, 8],
   "D'": [0, 10],
   "E'": [0, 12],
   "F'": [0, 13]
};

function getSymbolPosition(symbol) {
   if (typeof symbol !== "string") return null;
   const trimmed = symbol.trim();
   if (!trimmed) return null;
   return NOTE_SYMBOL_TO_POSITION[trimmed] || null;
}

function singleNoteEvent(symbol, duration = 1) {
   const position = getSymbolPosition(symbol);
   if (!position) {
      return {
         notes: [],
         duration
      };
   }

   return {
      notes: [position],
      duration
   };
}

function doubleNoteEvent(symbolA, symbolB, duration = 1) {
   const notes = [getSymbolPosition(symbolA), getSymbolPosition(symbolB)].filter(Boolean);
   return {
      notes,
      duration
   };
}

const SULTAN_GHALBHA_NOTES = [
   singleNoteEvent("A"),
   singleNoteEvent("B"),
   singleNoteEvent("C'"),
   doubleNoteEvent("E'", "C"),
   singleNoteEvent("E"),
   singleNoteEvent("F'"),
   doubleNoteEvent("E'", "C"),
   singleNoteEvent("E"),
   singleNoteEvent("F'"),
   doubleNoteEvent("E'", "C"),
   singleNoteEvent("E"),
   singleNoteEvent("F'"),
   singleNoteEvent("E'"),
   singleNoteEvent("F'"),
   singleNoteEvent("E'"),

   doubleNoteEvent("D", "D'"),
   singleNoteEvent("F"),
   singleNoteEvent("E'"),
   doubleNoteEvent("D", "D'"),
   singleNoteEvent("F"),
   singleNoteEvent("E'"),
   doubleNoteEvent("D", "D'"),
   singleNoteEvent("F"),
   singleNoteEvent("E'"),
   singleNoteEvent("D'"),
   singleNoteEvent("E'"),
   singleNoteEvent("D'"),

   doubleNoteEvent("C", "C'"),
   singleNoteEvent("E"),
   singleNoteEvent("D'"),
   doubleNoteEvent("C", "C'"),
   singleNoteEvent("E"),
   singleNoteEvent("D'"),
   doubleNoteEvent("C", "C'"),
   singleNoteEvent("E"),
   singleNoteEvent("B"),
   singleNoteEvent("A"),
   singleNoteEvent("G#"),
   singleNoteEvent("A"),
   singleNoteEvent("B"),
   doubleNoteEvent("D", "F"),

   singleNoteEvent(""),
   singleNoteEvent("F'"),
   singleNoteEvent(""),
   singleNoteEvent("E'")
];

// Chord shapes (fret positions for each string, -1 = muted, 0 = open)
const CHORDS = {
   C: {
      // string order: high E, B, G, D, A, low E
      // C major = C + E + G (x32010 in low-to-high order)
      frets: [0, 1, 0, 2, 3, -1],
      fingers: [null, 1, null, 2, 3, null],
      quality: "major",
      root: "C"
   },
   G: {
      frets: [3, 0, 0, 0, 2, 3],
      fingers: [2, null, null, null, 1, 3],
      quality: "major",
      root: "G"
   },
   Am: {
      frets: [0, 1, 2, 2, 0, -1],
      fingers: [null, 1, 2, 3, null, null],
      quality: "minor",
      root: "A"
   },
   F: {
      frets: [1, 1, 2, 3, 3, 1],
      fingers: [1, 1, 2, 3, 4, 1],
      quality: "major",
      root: "F"
   },
   D: {
      frets: [2, 3, 2, 0, -1, -1],
      fingers: [1, 3, 2, null, null, null],
      quality: "major",
      root: "D"
   },
   Em: {
      frets: [0, 0, 0, 2, 2, 0],
      fingers: [null, null, null, 1, 2, null],
      quality: "minor",
      root: "E"
   }
};

// Songs data - [string, fret, duration in ms]
const SONGS = {
   sultanGhalbha: {
      name: "سلطان قلب ها",
      tempo: 500,
      notes: SULTAN_GHALBHA_NOTES
   },
   greensleeves: {
      name: "لالایی بهاری",
      tempo: 400,
      notes: [
         [2, 0, 1],
         [1, 1, 1],
         [0, 3, 2],
         [0, 5, 1],
         [0, 3, 1],
         [0, 1, 2],
         [1, 0, 1],
         [2, 0, 1],
         [1, 1, 2],
         [2, 0, 1],
         [1, 1, 1],
         [0, 0, 2],
         [0, 0, 1],
         [1, 0, 1],
         [0, 1, 2],
         [0, 3, 1],
         [0, 5, 1],
         [0, 3, 2],
         [0, 1, 1],
         [1, 0, 1],
         [2, 0, 2],
         [1, 1, 1],
         [2, 0, 1],
         [1, 1, 2]
      ]
   },
   houseoftherisingsun: {
      name: "طلوع آرام",
      tempo: 350,
      notes: [
         [4, 0, 1],
         [3, 2, 1],
         [2, 2, 1],
         [1, 1, 1],
         [2, 2, 1],
         [3, 2, 1],
         [4, 2, 1],
         [3, 2, 1],
         [2, 0, 1],
         [1, 1, 1],
         [2, 0, 1],
         [3, 2, 1],
         [4, 0, 1],
         [3, 2, 1],
         [2, 1, 1],
         [1, 0, 1],
         [2, 1, 1],
         [3, 2, 1],
         [4, 2, 1],
         [3, 2, 1],
         [2, 2, 1],
         [1, 1, 1],
         [2, 2, 1],
         [3, 2, 1]
      ]
   },
   amazinggrace: {
      name: "آواز امید",
      tempo: 500,
      notes: [
         [3, 0, 1],
         [2, 0, 2],
         [1, 1, 1],
         [2, 0, 1],
         [1, 1, 2],
         [1, 0, 1],
         [2, 0, 3],
         [3, 2, 1],
         [3, 0, 2],
         [2, 0, 1],
         [1, 1, 1],
         [2, 0, 1],
         [1, 1, 2],
         [0, 0, 1],
         [0, 3, 3],
         [0, 3, 1],
         [0, 0, 2],
         [1, 1, 1],
         [2, 0, 1],
         [1, 1, 2],
         [1, 0, 1],
         [2, 0, 3]
      ]
   }
};

let soundEnabled = true;
let isPlaying = false;
let currentSong = "sultanGhalbha";
let songTimeout = null;
let noteIndex = 0;

// Single shared AudioContext
let audioCtx = null;
let compressor = null;

function getAudioContext() {
   if (!audioCtx) {
      audioCtx = new (window.AudioContext || window.webkitAudioContext)();
      // Add compressor to prevent clipping and reduce pops
      compressor = audioCtx.createDynamicsCompressor();
      compressor.threshold.value = -24;
      compressor.knee.value = 30;
      compressor.ratio.value = 12;
      compressor.attack.value = 0.003;
      compressor.release.value = 0.25;
      compressor.connect(audioCtx.destination);
   }
   // Resume if suspended (browsers require user interaction)
   if (audioCtx.state === "suspended") {
      audioCtx.resume();
   }
   return audioCtx;
}

// Initialize fretboard
function initFretboard() {
   const fretboard = document.getElementById("fretboard");

   for (let string = 0; string < STRINGS; string++) {
      for (let fret = 0; fret < FRETS; fret++) {
         const fretEl = document.createElement("div");
         fretEl.className = "fret";
         fretEl.dataset.string = string;
         fretEl.dataset.fret = fret;

         // Add fret markers
         if (string === 2 && [3, 5, 7, 9, 12].includes(fret)) {
            const marker = document.createElement("div");
            marker.className = "fret-marker";
            fretEl.appendChild(marker);
         }

         // Add note marker
         const noteMarker = document.createElement("div");
         noteMarker.className = "note-marker";
         noteMarker.textContent = getNoteAtPosition(string, fret);
         fretEl.appendChild(noteMarker);

         fretEl.addEventListener("click", () => playNote(string, fret));
         fretboard.appendChild(fretEl);
      }
   }
}

function getNoteAtPosition(string, fret) {
   const midi = getMidiNoteAtPosition(string, fret);
   const pitchClass = ((midi % 12) + 12) % 12;
   return NOTE_NAMES_FA[pitchClass];
}

function getFrequency(string, fret) {
   const baseFreqs = [329.63, 246.94, 196.0, 146.83, 110.0, 82.41];
   return baseFreqs[string] * Math.pow(2, fret / 12);
}

function parseNoteName(noteNameWithOctave) {
   const match = noteNameWithOctave.match(/^([A-G](?:#|b)?)(-?\d+)$/);
   if (!match) return null;

   let [, noteName, octaveStr] = match;
   noteName = FLAT_TO_SHARP[noteName] || noteName;

   const pitchClass = NOTE_NAMES.indexOf(noteName);
   if (pitchClass < 0) return null;

   const octave = parseInt(octaveStr, 10);
   return {
      pitchClass,
      octave
   };
}

function getMidiNoteAtPosition(string, fret) {
   const parsed = parseNoteName(STRING_NOTES[string]);
   if (!parsed) return 0;
   // MIDI mapping: C4 = 60
   const baseMidi = (parsed.octave + 1) * 12 + parsed.pitchClass;
   return baseMidi + fret;
}

function getPitchClassAtPosition(string, fret) {
   const midi = getMidiNoteAtPosition(string, fret);
   return ((midi % 12) + 12) % 12;
}

function playNote(string, fret, showMarker = true) {
   // Visual feedback first
   if (showMarker) {
      const fretEl = document.querySelector(`[data-string="${string}"][data-fret="${fret}"]`);
      if (fretEl) {
         const marker = fretEl.querySelector(".note-marker");
         marker.classList.add("show", "playing");
         setTimeout(() => marker.classList.remove("playing"), 300);
      }
   }

   if (!soundEnabled) return;

   try {
      const ctx = getAudioContext();
      const freq = getFrequency(string, fret);

      // Create oscillators for guitar-like tone
      const osc1 = ctx.createOscillator();
      const osc2 = ctx.createOscillator();
      const gainNode = ctx.createGain();
      const filter = ctx.createBiquadFilter();

      osc1.type = "triangle";
      osc2.type = "sine";
      osc1.frequency.value = freq;
      osc2.frequency.value = freq * 2;

      filter.type = "lowpass";
      filter.frequency.value = 1800;
      filter.Q.value = 0.7;

      osc1.connect(filter);
      osc2.connect(filter);
      filter.connect(gainNode);
      // Route through compressor to prevent clipping
      gainNode.connect(compressor);

      // Smoother guitar-like envelope with softer attack
      const now = ctx.currentTime;
      gainNode.gain.setValueAtTime(0.001, now);
      // Soft attack to prevent click
      gainNode.gain.exponentialRampToValueAtTime(0.15, now + 0.015);
      // Quick decay to sustain
      gainNode.gain.exponentialRampToValueAtTime(0.08, now + 0.1);
      // Gradual release
      gainNode.gain.exponentialRampToValueAtTime(0.001, now + 1.2);

      osc1.start(now);
      osc2.start(now);
      osc1.stop(now + 1.2);
      osc2.stop(now + 1.2);
   } catch (e) {
      console.log("خطای صوتی:", e);
   }
}

function showChord(chordName) {
   // Clear previous
   clearNotes();

   // Highlight active button
   document.querySelector(`[data-chord="${chordName}"]`).classList.add("active");

   const chord = CHORDS[chordName];
   const notesToPlay = [];

   chord.frets.forEach((fret, string) => {
      if (fret >= 0) {
         const actualFret = fret;
         const fretEl = document.querySelector(`[data-string="${string}"][data-fret="${actualFret}"]`);
         if (fretEl) {
            const marker = fretEl.querySelector(".note-marker");
            marker.classList.add("show");
            notesToPlay.push({
               string,
               fret: actualFret
            });
         }
      }
   });

   // Play chord with strum effect
   if (soundEnabled) {
      notesToPlay.reverse().forEach((note, i) => {
         setTimeout(() => playNote(note.string, note.fret, false), i * 40);
      });
   }
}

function getExpectedChordPitchClasses(rootNoteName, quality) {
   const rootIndex = NOTE_NAMES.indexOf(rootNoteName);
   if (rootIndex < 0) return [];

   const intervals = quality === "minor" ? [0, 3, 7] : [0, 4, 7];
   return intervals.map(interval => (rootIndex + interval) % 12).sort((a, b) => a - b);
}

function getActualChordPitchClasses(chordName) {
   const chord = CHORDS[chordName];
   const pcs = new Set();

   chord.frets.forEach((fret, string) => {
      if (fret >= 0) {
         pcs.add(getPitchClassAtPosition(string, fret));
      }
   });

   return [...pcs].sort((a, b) => a - b);
}

function verifyChordMappings() {
   const mismatches = [];

   Object.entries(CHORDS).forEach(([name, chord]) => {
      if (!chord.root || !chord.quality) return;

      const expected = getExpectedChordPitchClasses(chord.root, chord.quality);
      const actual = getActualChordPitchClasses(name);

      const expectedKey = expected.join(",");
      const actualKey = actual.join(",");

      if (expectedKey !== actualKey) {
         mismatches.push({
            chord: name,
            expected: expected.map(i => NOTE_NAMES[i]),
            actual: actual.map(i => NOTE_NAMES[i])
         });
      }
   });

   if (mismatches.length > 0) {
      console.error("ناسازگاری تئوری آکوردها:", mismatches);
   } else {
      console.info("اعتبارسنجی آکوردها موفق بود: نگاشت آکوردها با تئوری موسیقی سازگار است.");
   }
}

function clearNotes() {
   document.querySelectorAll(".note-marker").forEach(m => m.classList.remove("show"));
   document.querySelectorAll(".chord-btn").forEach(b => b.classList.remove("active"));
}

function normalizeSongEvent(rawEvent) {
   if (Array.isArray(rawEvent)) {
      const [string, fret, duration] = rawEvent;
      const validSingle = Number.isInteger(string) && Number.isInteger(fret);
      return {
         notes: validSingle ? [[string, fret]] : [],
         duration: duration || 1
      };
   }

   if (rawEvent && Array.isArray(rawEvent.notes)) {
      const validNotes = rawEvent.notes.filter(note => {
         return Array.isArray(note) && note.length === 2 && Number.isInteger(note[0]) && Number.isInteger(note[1]);
      });

      return {
         notes: validNotes,
         duration: rawEvent.duration || 1
      };
   }

   return null;
}

// Song player functions
function playSong() {
   if (isPlaying) {
      stopSong();
      return;
   }

   // Initialize audio context on user interaction
   getAudioContext();

   isPlaying = true;
   noteIndex = 0;
   document.getElementById("playBtn").textContent = "■";
   document.getElementById("playBtn").classList.add("playing");
   playNextNote();
}

function stopSong() {
   isPlaying = false;
   if (songTimeout) {
      clearTimeout(songTimeout);
      songTimeout = null;
   }
   noteIndex = 0;
   document.getElementById("playBtn").textContent = "▶";
   document.getElementById("playBtn").classList.remove("playing");
   document.getElementById("progressBar").style.width = "0%";
   clearNotes();
}

function playNextNote() {
   if (!isPlaying) return;

   const song = SONGS[currentSong];
   if (noteIndex >= song.notes.length) {
      stopSong();
      return;
   }

   const event = normalizeSongEvent(song.notes[noteIndex]);
   if (!event) {
      stopSong();
      return;
   }

   // Clear previous and play current
   clearNotes();
   event.notes.forEach(([string, fret]) => {
      const fretEl = document.querySelector(`[data-string="${string}"][data-fret="${fret}"]`);
      if (fretEl) {
         const marker = fretEl.querySelector(".note-marker");
         marker.classList.add("show", "playing");
      }
      playNote(string, fret, false);
   });

   // Update progress
   const progress = ((noteIndex + 1) / song.notes.length) * 100;
   document.getElementById("progressBar").style.width = progress + "%";

   noteIndex++;
   songTimeout = setTimeout(playNextNote, song.tempo * event.duration);
}

function changeSong(songKey) {
   stopSong();
   currentSong = songKey;
   document.getElementById("songTitle").textContent = SONGS[songKey].name;
}

// Event listeners
document.querySelectorAll(".chord-btn").forEach(btn => {
   btn.addEventListener("click", () => {
      stopSong();
      showChord(btn.dataset.chord);
   });
});

document.getElementById("soundToggle").addEventListener("click", function () {
   soundEnabled = !soundEnabled;
   this.classList.toggle("active", soundEnabled);
   // Initialize audio context when enabling sound
   if (soundEnabled) {
      getAudioContext();
   }
});

document.getElementById("clearBtn").addEventListener("click", () => {
   stopSong();
   clearNotes();
});

document.getElementById("playBtn").addEventListener("click", playSong);

document.getElementById("songSelect").addEventListener("change", function () {
   changeSong(this.value);
});

// Initialize
initFretboard();
verifyChordMappings();

// Pre-initialize audio context on first user interaction
document.addEventListener("click", function initAudio() {
   getAudioContext();
   document.removeEventListener("click", initAudio);
}, {
   once: true
});

// Mobile menu toggle
const mobileMenuBtn = document.querySelector(".mobile-menu-btn");
const navLinks = document.querySelector(".nav-links");
const navCta = document.querySelector(".nav-cta");

mobileMenuBtn.addEventListener("click", () => {
   const isOpen = navLinks.classList.toggle("active");
   navCta.classList.toggle("active", isOpen);
   mobileMenuBtn.textContent = isOpen ? "✕" : "☰";

   // Dynamically position CTA below nav-links
   if (isOpen) {
      setTimeout(() => {
         const navLinksHeight = navLinks.offsetHeight;
         navCta.style.top = `calc(100% + ${navLinksHeight}px)`;
      }, 10);
   }
});

// Close mobile menu when clicking a link
navLinks.querySelectorAll("a").forEach(link => {
   link.addEventListener("click", () => {
      navLinks.classList.remove("active");
      navCta.classList.remove("active");
      mobileMenuBtn.textContent = "☰";
   });
});

// Pricing toggle
const PRICING = {
   monthly: {
      price: 1450000,
      period: "در ماه",
      billed: "پرداخت ماهانه",
      savings: ""
   },
   quarterly: {
      price: 1250000,
      period: "در ماه",
      billed: `پرداخت هر ۳ ماه: ${formatToman(3750000)} تومان`,
      savings: "۱۵ درصد صرفه جویی"
   },
   yearly: {
      price: 980000,
      period: "در ماه",
      billed: `پرداخت سالانه: ${formatToman(11760000)} تومان`,
      savings: "۳۰ درصد صرفه جویی"
   }
};

function updatePricing(billing) {
   const plan = PRICING[billing];
   const priceEl = document.getElementById("proPrice");
   const billedEl = document.getElementById("proBilled");
   const savingsEl = document.getElementById("proSavings");

   if (!priceEl || !billedEl || !savingsEl) {
      return;
   }

   // Update active button
   document.querySelectorAll(".billing-option").forEach(btn => {
      btn.classList.toggle("active", btn.dataset.billing === billing);
   });

   // Update price display
   // Animate price change
   priceEl.style.opacity = "0";
   priceEl.style.transform = "translateY(-10px)";

   setTimeout(() => {
      priceEl.textContent = formatToman(plan.price);
      billedEl.textContent = plan.billed;
      savingsEl.textContent = plan.savings;

      priceEl.style.opacity = "1";
      priceEl.style.transform = "translateY(0)";
   }, 150);
}

const billingButtons = document.querySelectorAll(".billing-option");

if (billingButtons.length) {
   billingButtons.forEach(btn => {
      btn.addEventListener("click", () => updatePricing(btn.dataset.billing));
   });

   updatePricing("monthly");

   // Add transition to price element
   const proPrice = document.getElementById("proPrice");
   if (proPrice) {
      proPrice.style.transition = "all 0.15s ease";
   }
}

// FAQ premium accordion
const faqItems = Array.from(document.querySelectorAll(".faq-item"));

function openFaqItem(item) {
   const trigger = item.querySelector(".faq-trigger");
   const panel = item.querySelector(".faq-panel");
   if (!trigger || !panel) return;

   item.classList.add("open");
   trigger.setAttribute("aria-expanded", "true");
   panel.style.maxHeight = panel.scrollHeight + "px";
}

function closeFaqItem(item) {
   const trigger = item.querySelector(".faq-trigger");
   const panel = item.querySelector(".faq-panel");
   if (!trigger || !panel) return;

   item.classList.remove("open");
   trigger.setAttribute("aria-expanded", "false");
   panel.style.maxHeight = "0px";
}

faqItems.forEach(item => {
   const trigger = item.querySelector(".faq-trigger");
   if (!trigger) return;

   closeFaqItem(item);

   trigger.addEventListener("click", () => {
      const isOpen = item.classList.contains("open");

      faqItems.forEach(other => {
         if (other !== item) {
            closeFaqItem(other);
         }
      });

      if (isOpen) {
         closeFaqItem(item);
      } else {
         openFaqItem(item);
      }
   });
});

window.addEventListener("resize", () => {
   faqItems.forEach(item => {
      if (item.classList.contains("open")) {
         const panel = item.querySelector(".faq-panel");
         if (panel) {
            panel.style.maxHeight = panel.scrollHeight + "px";
         }
      }
   });
});
