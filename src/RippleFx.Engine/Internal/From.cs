using System;
using System.Collections.Generic;
using RippleFx.Engine.Interop;

namespace RippleFx.Engine.Internal
{
    /// <summary>
    /// The engine's structs copied into this binding's records. Every reader
    /// here copies: nothing it answers points into memory the engine owns.
    /// </summary>
    internal static unsafe class From
    {
        public static Track Track(RippleTrack* t) => new Track
        {
            Media = t->media,
            Lane = t->lane,
            Start = t->start,
            ClipStart = t->clip_start,
            ClipEnd = Read.Opt(t->clip_end),
            Volume = t->volume,
            Speed = t->speed,
            FadeIn = t->fade_in,
            FadeOut = t->fade_out,
            Mode = (TrackMode)t->mode,
            Interval = t->interval,
        };

        public static Soundscape Soundscape(RippleSoundscape* s) => new Soundscape
        {
            Id = Read.Str(s->id),
            Name = Read.Str(s->name),
            Length = s->length,
            Looping = s->looping != 0,
            FadeIn = s->fade_in,
            FadeOut = s->fade_out,
            Volume = s->volume,
            Tied = s->tied != 0,
            Tracks = Read.List(s->tracks, s->track_count, item => Track((RippleTrack*)item)),
        };

        public static Entry Entry(RippleEntry* e) => new Entry(Read.Str(e->id), Read.Str(e->name), Read.Str(e->path));

        private static IReadOnlyList<Entry> Entries(RippleEntry* items, UIntPtr count) =>
            Read.List(items, count, item => Entry((RippleEntry*)item));

        public static Manifest Manifest(RippleManifest* m) => new Manifest
        {
            Format = m->format,
            Generator = Read.Opt(m->generator),
            TextFormat = Read.Str(m->text_format),
            Project = new Project
            {
                Title = Read.Str(m->project.title),
                Description = Read.Str(m->project.description),
                DefaultLocale = Read.Str(m->project.default_locale),
                Locales = Read.Texts(m->project.locales, m->project.locale_count),
                Cover = Read.Opt(m->project.cover),
            },
            Entries = Read.Indices(m->entries, m->entry_count),
            Boards = Entries(m->boards, m->board_count),
            Passages = Entries(m->passages, m->passage_count),
            Components = Entries(m->components, m->component_count),
            Scripts = Entries(m->scripts, m->script_count),
            Media = Entries(m->media, m->media_count),
            Machines = Entries(m->machines, m->machine_count),
            Soundscapes = Entries(m->soundscapes, m->soundscape_count),
            Cvars = Read.List(m->cvars, m->cvar_count, item =>
            {
                var c = (RippleCvar*)item;
                return new Cvar(Read.Str(c->name), Read.Str(c->type_name), Arena.Read(c->default_value));
            }),
            Needs = Read.Texts(m->needs, m->need_count),
            Permissions = Read.Texts(m->permissions, m->permission_count),
        };

        private static Port Port(RipplePort* p) => new Port(
            Read.Str(p->id),
            (PortKind)p->kind,
            Read.List(p->edges, p->edge_count, item => new Edge(((RippleEdge*)item)->node, ((RippleEdge*)item)->port)));

        public static Node Node(RippleNode* n) => new Node
        {
            Id = Read.Str(n->id),
            Type = Read.Str(n->type_name),
            Title = Read.Str(n->title),
            Inputs = Read.List(n->inputs, n->input_count, item => Port((RipplePort*)item)),
            Outputs = Read.List(n->outputs, n->output_count, item => Port((RipplePort*)item)),
            Data = Arena.Read(n->data),
            Metadata = Arena.Read(n->metadata),
        };

        public static Board Board(RippleBoard* b) => new Board
        {
            Id = Read.Str(b->id),
            Name = Read.Str(b->name),
            Start = b->start,
            Prime = Read.Indices(b->prime, b->prime_count),
            Nodes = Read.List(b->nodes, b->node_count, item => Node((RippleNode*)item)),
        };

        public static Passage Passage(RipplePassage* p) => new Passage
        {
            Node = Read.Str(p->node),
            Title = Read.Str(p->title),
            Text = Read.Str(p->text),
            Choices = Read.List(p->choices, p->choice_count, item =>
            {
                var c = (RippleChoice*)item;
                return new Choice(Read.Str(c->port), Read.Str(c->text), c->available != 0);
            }),
            Sources = p->has_sources == 0
                ? null
                : Read.List(p->sources, p->source_count, item =>
                    new LocaleText(Read.Str(((RippleLocaleText*)item)->locale), Read.Str(((RippleLocaleText*)item)->text))),
            Snippets = Read.List(p->snippets, p->snippet_count, item =>
                new Snippet(Read.Str(((RippleSnippet*)item)->code), ((RippleSnippet*)item)->is_inline != 0)),
            Media = Read.List(p->media, p->media_count, item =>
                new MediaLink(Read.Str(((RippleMediaLink*)item)->link), ((RippleMediaLink*)item)->media)),
            Untranslated = Read.Opt(p->untranslated),
            Soundscape = p->soundscape == null ? null : Soundscape(p->soundscape),
            Silence = p->silence != 0,
            Timer = p->timer == null
                ? null
                : new ChoiceTimer
                {
                    AfterMs = p->timer->after_ms,
                    TimeoutMs = p->timer->timeout_ms,
                    Visible = p->timer->visible != 0,
                    Default = checked((int)p->timer->default_choice),
                },
        };

        public static Prompt Prompt(RipplePrompt* p) => new Prompt(
            Read.Str(p->node),
            Read.Str(p->title),
            Read.List(p->fields, p->field_count, item =>
            {
                var f = (RipplePromptField*)item;
                return new PromptField(Read.Str(f->name), Read.Str(f->type_name), Arena.Read(f->default_value))
                {
                    Label = Read.Str(f->label),
                    Placeholder = Read.Str(f->placeholder),
                    Optional = f->optional != 0,
                    Options = Read.List(f->options, f->option_count, option =>
                    {
                        var o = (RipplePromptOption*)option;
                        return new PromptOption(Read.Str(o->id), Read.Str(o->label), Arena.Read(o->value));
                    }),
                };
            }));

        public static Ambience Ambience(RippleAmbience* a) =>
            new Ambience(Soundscape(&a->soundscape), a->board, Read.Str(a->node));

        private static IReadOnlyList<TraceValue> Traced(RippleTraceValue* items, UIntPtr count) =>
            Read.List(items, count, item =>
            {
                var t = (RippleTraceValue*)item;
                return new TraceValue
                {
                    FromNode = Read.Str(t->from_node),
                    FromPort = Read.Str(t->from_port),
                    ToNode = Read.Opt(t->to_node),
                    ToPort = Read.Opt(t->to_port),
                    Payload = (PayloadKind)t->payload,
                    Value = t->payload == (uint)RipplePayloadKind.RIPPLE_PAYLOAD_KIND_PRESENT ? Arena.Read(t->value) : Value.Null,
                };
            });

        public static RunEvent RunEvent(RippleRunEvent* e) => new RunEvent
        {
            Kind = (RunEventKind)e->kind,
            Run = e->at.run,
            Board = e->at.board,
            Sequence = e->at.sequence,
            Node = Read.Str(e->node),
            Step = e->step,
            Entry = Read.Str(e->entry),
            Primed = Read.Texts(e->primed, e->primed_count),
            EnteredVia = Read.Opt(e->entered_via),
            Pulled = e->pulled != 0,
            Inputs = Traced(e->inputs, e->input_count),
            Outcome = e->outcome,
            Outputs = Traced(e->outputs, e->output_count),
            Flowed = Read.Texts(e->flowed, e->flowed_count),
            Error = Read.Opt(e->error),
            Diagnostic = e->diagnostic == null
                ? null
                : new RunDiagnostic
                {
                    Issue = e->diagnostic->issue,
                    Message = Read.Str(e->diagnostic->message),
                    Node = Read.Opt(e->diagnostic->node),
                    Port = Read.Opt(e->diagnostic->port),
                    Wires = Read.Opt(e->diagnostic->wires),
                },
            Level = e->level,
            Message = Read.Str(e->message),
            Data = Arena.Read(e->data),
            Steps = e->steps,
            Cancelled = e->cancelled != 0,
            FailedNode = Read.Opt(e->failed_node),
        };

        public static CallbackEvent Callback(RippleCallbackEvent* c) => new CallbackEvent
        {
            Callback = Read.Str(c->callback),
            Event = c->@event,
            EventName = Read.Str(Native.ripple_callback_kind_name(c->@event)),
            Board = Read.Opt(c->board),
            Node = Read.Opt(c->node),
            Name = Read.Opt(c->name),
            From = Arena.Read(c->from),
            To = Arena.Read(c->to),
            Index = Read.Opt(c->index),
        };

        public static Entity Entity(RippleEntity* e) => new Entity
        {
            Key = Read.Str(e->key),
            Component = Read.Opt(e->component),
            Label = Read.Str(e->label),
            Values = Arena.Read(e->values),
        };

        public static Memory Memory(RippleMemory* m) => new Memory
        {
            Nodes = Arena.Read(m->nodes),
            Machines = Read.List(m->machines, m->machine_count, item =>
                new KeyValuePair<string, string>(Read.Str(((RippleMachineState*)item)->machine), Read.Str(((RippleMachineState*)item)->state))),
        };

        public static CvarSnapshot CvarSnapshot(RippleCvarSnapshot* c) =>
            new CvarSnapshot(Read.Str(c->name), Read.Str(c->type_name), Arena.Read(c->default_value), Arena.Read(c->value));

        public static AudioCommand Audio(RippleAudioCommand* a) => new AudioCommand
        {
            Kind = (AudioKind)a->kind,
            Soundscape = a->soundscape == null ? null : Soundscape(a->soundscape),
            FadeMs = a->fade_ms,
            Media = a->media,
            Volume = a->volume,
            Speed = a->speed,
            Node = Read.Opt(a->node),
        };

        public static Effect Effect(RippleEffect* e)
        {
            var data = e->data;
            switch ((RippleEffectKind)e->kind)
            {
                case RippleEffectKind.RIPPLE_EFFECT_KIND_TRACE:
                    return new Effect.Trace(RunEvent(data.trace));
                case RippleEffectKind.RIPPLE_EFFECT_KIND_CALLBACK:
                    return new Effect.Callback(Callback(data.callback));
                case RippleEffectKind.RIPPLE_EFFECT_KIND_JUMPED:
                    return new Effect.Jumped(data.jumped->board, Read.Opt(data.jumped->node));
                case RippleEffectKind.RIPPLE_EFFECT_KIND_AMBIENCE:
                    return new Effect.AmbienceChanged(data.ambience == null ? null : Ambience(data.ambience));
                case RippleEffectKind.RIPPLE_EFFECT_KIND_AUDIO:
                    return new Effect.Audio(Audio(data.audio));
                case RippleEffectKind.RIPPLE_EFFECT_KIND_CHANGED:
                    {
                        var c = data.changed;
                        return new Effect.Changed(
                            c->has_entities == 0 ? null : Read.List(c->entities, c->entity_count, item => Entity((RippleEntity*)item)),
                            c->memory == null ? null : Memory(c->memory),
                            c->has_cvars == 0 ? null : Read.List(c->cvars, c->cvar_count, item => CvarSnapshot((RippleCvarSnapshot*)item)));
                    }
                case RippleEffectKind.RIPPLE_EFFECT_KIND_SHOW:
                    return new Effect.Show(Passage(data.show));
                case RippleEffectKind.RIPPLE_EFFECT_KIND_ASK:
                    return new Effect.Ask(Prompt(data.ask));
                case RippleEffectKind.RIPPLE_EFFECT_KIND_HOLD:
                    return new Effect.Hold(data.hold_ms);
                case RippleEffectKind.RIPPLE_EFFECT_KIND_ENDED:
                    return new Effect.Ended(Read.Opt(data.ended->title), data.ended->ran_out != 0);
                case RippleEffectKind.RIPPLE_EFFECT_KIND_FAILED:
                    return new Effect.Failed(Read.Str(data.failed->message), Read.Opt(data.failed->node));
                case RippleEffectKind.RIPPLE_EFFECT_KIND_PERMIT:
                    return new Effect.Permit(Read.List(data.permit->kinds, data.permit->kind_count, item => (PermissionKind)(*(uint*)item)));
                case RippleEffectKind.RIPPLE_EFFECT_KIND_NOTICE:
                    {
                        var n = data.notice;
                        return new Effect.Notice(Read.Str(n->code), (PermissionKind)n->kind, Read.Opt(n->node), Read.Str(n->message));
                    }
                default:
                    return new Effect.Stopped();
            }
        }

        public static IReadOnlyList<Effect> Effects(RippleEffect* items, UIntPtr count) =>
            Read.List(items, count, item => Effect((RippleEffect*)item));

        public static PythonResult Python(RipplePythonResult* r) => new PythonResult(
            Arena.Read(r->value),
            Read.Str(r->output),
            r->error == null
                ? null
                : new PythonFailure
                {
                    Kind = r->error->kind,
                    Exception = Read.Opt(r->error->exception),
                    Message = Read.Str(r->error->message),
                    Line = Read.Opt(r->error->line),
                    Traceback = Read.Opt(r->error->traceback),
                });
    }
}
