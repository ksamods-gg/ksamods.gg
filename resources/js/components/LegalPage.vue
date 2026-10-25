<script setup lang="ts">
import PageMeta from '@/components/PageMeta.vue';
import AppLayout from '@/layouts/AppLayout.vue';

defineProps<{ title: string; updated: string; description: string }>();
</script>

<template>
    <PageMeta :title="title" :description="description" />

    <AppLayout>
        <article class="mx-auto max-w-3xl">
            <h1 class="text-2xl font-semibold tracking-tight">{{ title }}</h1>
            <p class="mt-1 text-sm text-[#706f6c] dark:text-[#A1A09A]">
                Last updated {{ updated }}
            </p>
            <div class="markdown mt-6 text-sm">
                <slot />
            </div>
        </article>
    </AppLayout>
</template>
