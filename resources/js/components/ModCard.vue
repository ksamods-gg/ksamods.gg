<script setup lang="ts">
import { Link } from '@inertiajs/vue3';
import ModIcon from '@/components/ModIcon.vue';
import { formatDate, formatNumber } from '@/lib/format';
import { show } from '@/routes/mods';
import type { ModSummary } from '@/types';

defineProps<{ mod: ModSummary }>();
</script>

<template>
    <Link
        :href="show(mod.id)"
        class="flex h-full flex-col rounded-lg bg-white p-4 shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.16)] transition-shadow hover:shadow-[inset_0px_0px_0px_1px_rgba(26,26,0,0.4)] dark:bg-[#161615] dark:shadow-[inset_0px_0px_0px_1px_#fffaed2d] dark:hover:shadow-[inset_0px_0px_0px_1px_#fffaed5a]"
    >
        <div class="flex gap-3">
            <ModIcon :name="mod.name" :src="mod.icon" class="size-12 text-lg" />
            <div class="min-w-0">
                <h2 class="truncate font-medium">
                    {{ mod.name }}
                </h2>
                <p class="truncate text-xs text-[#706f6c] dark:text-[#A1A09A]">
                    by {{ mod.authors.join(', ') }}
                </p>
            </div>
        </div>

        <p
            class="mt-3 line-clamp-3 flex-1 text-sm text-[#4a4a46] dark:text-[#c4c3be]"
        >
            {{ mod.abstract }}
        </p>

        <div
            class="mt-4 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-[#706f6c] dark:text-[#A1A09A]"
        >
            <span
                v-if="mod.type === 'mod-loader'"
                class="font-medium text-[#f53003] dark:text-[#FF4433]"
            >
                Mod loader
            </span>
            <span v-if="mod.version">v{{ mod.version }}</span>
            <span>{{ formatNumber(mod.downloads) }} downloads</span>
            <span>Updated {{ formatDate(mod.updated_at) }}</span>
        </div>
    </Link>
</template>
