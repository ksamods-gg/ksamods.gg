<script setup lang="ts">
import { computed, ref } from 'vue';
import { formatDate, formatNumber } from '@/lib/format';

/**
 * One series over time: a 2px line over a faint area, a crosshair and tooltip on hover or with
 * the arrow keys, and the same numbers as a table. The title names the series, so there is no
 * legend.
 */
const props = defineProps<{
    title: string;
    points: { date: string; value: number }[];
    // A fixed scale, such as 1 to 5 stars; otherwise from 0 to the highest value.
    min?: number;
    max?: number;
    format?: (value: number) => string;
}>();

const W = 640;
const H = 200;
const PAD = { top: 12, right: 12, bottom: 24, left: 52 };

const show = (value: number) =>
    props.format ? props.format(value) : formatNumber(value);

const scale = computed(() => {
    const values = props.points.map((p) => p.value);
    const low = props.min ?? 0;
    const high = props.max ?? Math.max(1, ...values) * 1.1;
    const times = props.points.map((p) => new Date(p.date).getTime());
    const first = Math.min(...times);
    const last = Math.max(...times);

    return {
        x: (date: string) =>
            PAD.left +
            (last === first
                ? (W - PAD.left - PAD.right) / 2
                : ((new Date(date).getTime() - first) / (last - first)) *
                  (W - PAD.left - PAD.right)),
        y: (value: number) =>
            H -
            PAD.bottom -
            ((value - low) / (high - low || 1)) * (H - PAD.top - PAD.bottom),
        ticks: [0, 1, 2, 3].map((i) => low + ((high - low) * i) / 3),
    };
});

const line = computed(() =>
    props.points
        .map(
            (p, i) =>
                `${i ? 'L' : 'M'}${scale.value.x(p.date).toFixed(1)},${scale.value.y(p.value).toFixed(1)}`,
        )
        .join(' '),
);
const area = computed(() =>
    props.points.length
        ? `${line.value} L${scale.value.x(props.points.at(-1)!.date).toFixed(1)},${H - PAD.bottom} L${scale.value.x(props.points[0].date).toFixed(1)},${H - PAD.bottom} Z`
        : '',
);

// The point under the pointer or chosen with the arrow keys.
const active = ref<number | null>(null);

function pointAt(event: PointerEvent) {
    const box = (event.currentTarget as SVGElement).getBoundingClientRect();
    const x = ((event.clientX - box.left) / box.width) * W;
    let nearest = 0;

    props.points.forEach((p, i) => {
        if (
            Math.abs(scale.value.x(p.date) - x) <
            Math.abs(scale.value.x(props.points[nearest].date) - x)
        ) {
            nearest = i;
        }
    });

    active.value = nearest;
}

function step(event: KeyboardEvent) {
    const last = props.points.length - 1;
    const next = {
        ArrowRight: Math.min(last, (active.value ?? -1) + 1),
        ArrowLeft: Math.max(0, (active.value ?? last + 1) - 1),
        Home: 0,
        End: last,
    }[event.key];

    if (next !== undefined) {
        event.preventDefault();
        active.value = next;
    }
}

const point = computed(() =>
    active.value === null ? null : props.points[active.value],
);

const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
</script>

<template>
    <figure class="flex flex-col gap-2">
        <figcaption class="text-sm font-semibold">{{ title }}</figcaption>

        <div class="relative">
            <svg
                :viewBox="`0 0 ${W} ${H}`"
                class="w-full overflow-visible text-[#119087] focus:outline-none focus-visible:ring-2 focus-visible:ring-accent dark:text-[#2aa9a0]"
                role="img"
                :aria-label="`${title}: from ${show(points[0].value)} on ${formatDate(points[0].date)} to ${show(points.at(-1)!.value)} on ${formatDate(points.at(-1)!.date)}. Use the arrow keys to read each day.`"
                tabindex="0"
                @pointermove="pointAt"
                @pointerleave="active = null"
                @keydown="step"
                @blur="active = null"
            >
                <!-- Recessive grid and axis labels. -->
                <g
                    v-for="tick in scale.ticks"
                    :key="tick"
                    class="text-[#19140014] dark:text-[#3E3E3A]"
                >
                    <line
                        :x1="PAD.left"
                        :x2="W - PAD.right"
                        :y1="scale.y(tick)"
                        :y2="scale.y(tick)"
                        stroke="currentColor"
                    />
                    <text
                        :x="PAD.left - 8"
                        :y="scale.y(tick) + 4"
                        text-anchor="end"
                        class="fill-[#706f6c] text-xs dark:fill-[#A1A09A]"
                    >
                        {{ show(tick) }}
                    </text>
                </g>
                <text
                    :x="PAD.left"
                    :y="H - 6"
                    class="fill-[#706f6c] text-xs dark:fill-[#A1A09A]"
                >
                    {{ formatDate(points[0].date) }}
                </text>
                <text
                    v-if="points.length > 1"
                    :x="W - PAD.right"
                    :y="H - 6"
                    text-anchor="end"
                    class="fill-[#706f6c] text-xs dark:fill-[#A1A09A]"
                >
                    {{ formatDate(points.at(-1)!.date) }}
                </text>

                <path :d="area" fill="currentColor" fill-opacity="0.1" />
                <path
                    :d="line"
                    fill="none"
                    stroke="currentColor"
                    stroke-width="2"
                    stroke-linejoin="round"
                    stroke-linecap="round"
                    vector-effect="non-scaling-stroke"
                />
                <!-- A lone point has no line to show it. -->
                <circle
                    v-if="points.length === 1"
                    :cx="scale.x(points[0].date)"
                    :cy="scale.y(points[0].value)"
                    r="4"
                    fill="currentColor"
                />

                <g v-if="point">
                    <line
                        :x1="scale.x(point.date)"
                        :x2="scale.x(point.date)"
                        :y1="PAD.top"
                        :y2="H - PAD.bottom"
                        class="text-[#1915014a] dark:text-[#62605b]"
                        stroke="currentColor"
                        stroke-dasharray="3 3"
                    />
                    <circle
                        :cx="scale.x(point.date)"
                        :cy="scale.y(point.value)"
                        r="5"
                        fill="currentColor"
                        class="stroke-white dark:stroke-[#161615]"
                        stroke-width="2"
                    />
                </g>
            </svg>

            <div
                v-if="point"
                role="status"
                class="pointer-events-none absolute top-0 rounded-md bg-white px-2.5 py-1.5 text-xs shadow-[0_8px_24px_rgba(0,0,0,0.12),inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] dark:bg-[#161615] dark:shadow-[0_8px_24px_rgba(0,0,0,0.5),inset_0px_0px_0px_1px_#fffaed2d]"
                :style="{
                    left: `${(scale.x(point.date) / W) * 100}%`,
                    transform: `translateX(${scale.x(point.date) > W / 2 ? 'calc(-100% - 8px)' : '8px'})`,
                }"
            >
                <span class="font-semibold tabular-nums">{{
                    show(point.value)
                }}</span>
                <span :class="['block', muted]">{{
                    formatDate(point.date)
                }}</span>
            </div>
        </div>

        <details :class="['text-xs', muted]">
            <summary class="cursor-pointer">Show as a table</summary>
            <table class="mt-2 w-full max-w-sm text-left tabular-nums">
                <thead>
                    <tr>
                        <th class="py-1 font-medium">Date</th>
                        <th class="py-1 text-right font-medium">{{ title }}</th>
                    </tr>
                </thead>
                <tbody>
                    <tr v-for="row in points" :key="row.date">
                        <td class="py-0.5">{{ formatDate(row.date) }}</td>
                        <td class="py-0.5 text-right">{{ show(row.value) }}</td>
                    </tr>
                </tbody>
            </table>
        </details>
    </figure>
</template>
