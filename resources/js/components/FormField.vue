<script setup lang="ts">
defineProps<{
    label: string;
    for: string;
    hint?: string;
    error?: string;
    required?: boolean;
}>();
</script>

<template>
    <div class="flex flex-col gap-1.5">
        <label :for="$props.for" class="text-sm font-medium">
            {{ label }}
            <span
                v-if="!required"
                class="font-normal text-[#706f6c] dark:text-[#A1A09A]"
                >(optional)</span
            >
        </label>
        <slot />
        <p
            v-if="error"
            :id="`${$props.for}-error`"
            class="text-xs text-[#f53003] dark:text-[#FF4433]"
        >
            {{ error }}
        </p>
        <p
            v-else-if="hint"
            :id="`${$props.for}-hint`"
            class="text-xs text-[#706f6c] dark:text-[#A1A09A]"
        >
            {{ hint }}
        </p>
    </div>
</template>
