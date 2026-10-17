<script setup lang="ts">
import { ref, useTemplateRef } from 'vue';

const props = defineProps<{ images: string[]; name: string }>();

// The open image, shown full size in a modal dialog; Escape closing it comes with <dialog>.
const current = ref(0);
const viewer = useTemplateRef<HTMLDialogElement>('viewer');

function open(index: number) {
    current.value = index;
    viewer.value?.showModal();
}

function step(by: number) {
    current.value =
        (current.value + by + props.images.length) % props.images.length;
}

// A click on the backdrop lands on the dialog itself, outside its content.
function closeOnBackdrop(event: MouseEvent) {
    if (event.target === viewer.value) {
        viewer.value?.close();
    }
}

const control =
    'flex size-10 items-center justify-center rounded-full bg-black/60 text-white hover:bg-black/80';
</script>

<template>
    <ul class="grid grid-cols-2 gap-3 sm:grid-cols-3" aria-label="Screenshots">
        <li v-for="(image, index) in images" :key="image">
            <button
                type="button"
                class="block w-full overflow-hidden rounded-lg shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] hover:opacity-90 dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d]"
                :aria-label="`Open image ${index + 1} of ${images.length}`"
                @click="open(index)"
            >
                <img
                    :src="image"
                    alt=""
                    loading="lazy"
                    class="aspect-video w-full bg-[#f4f3ef] object-cover dark:bg-[#232321]"
                />
            </button>
        </li>
    </ul>

    <dialog
        ref="viewer"
        :aria-label="`${name} images`"
        class="m-auto max-h-none max-w-none bg-transparent p-0 backdrop:bg-black/80"
        @click="closeOnBackdrop"
        @keydown.left="step(-1)"
        @keydown.right="step(1)"
    >
        <div class="relative flex items-center justify-center">
            <img
                :src="images[current]"
                :alt="`${name}, image ${current + 1} of ${images.length}`"
                class="max-h-[85vh] max-w-[92vw] rounded-lg object-contain"
            />
            <button
                type="button"
                :class="[control, 'absolute top-2 right-2']"
                aria-label="Close"
                @click="viewer?.close()"
            >
                <svg
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    stroke-width="1.75"
                    stroke-linecap="round"
                    aria-hidden="true"
                    class="size-5"
                >
                    <path d="M6 6l12 12M18 6 6 18" />
                </svg>
            </button>
            <template v-if="images.length > 1">
                <button
                    type="button"
                    :class="[control, 'absolute left-2']"
                    aria-label="Previous image"
                    @click="step(-1)"
                >
                    <svg
                        viewBox="0 0 24 24"
                        fill="none"
                        stroke="currentColor"
                        stroke-width="1.75"
                        stroke-linecap="round"
                        stroke-linejoin="round"
                        aria-hidden="true"
                        class="size-5"
                    >
                        <path d="m15 18-6-6 6-6" />
                    </svg>
                </button>
                <button
                    type="button"
                    :class="[control, 'absolute right-2']"
                    aria-label="Next image"
                    @click="step(1)"
                >
                    <svg
                        viewBox="0 0 24 24"
                        fill="none"
                        stroke="currentColor"
                        stroke-width="1.75"
                        stroke-linecap="round"
                        stroke-linejoin="round"
                        aria-hidden="true"
                        class="size-5"
                    >
                        <path d="m9 18 6-6-6-6" />
                    </svg>
                </button>
                <p
                    class="absolute bottom-2 rounded-full bg-black/60 px-2.5 py-0.5 text-xs text-white tabular-nums"
                >
                    {{ current + 1 }} / {{ images.length }}
                </p>
            </template>
        </div>
    </dialog>
</template>
