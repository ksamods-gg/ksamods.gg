<script setup lang="ts">
import { Head, Link } from '@inertiajs/vue3';
import AppLayout from '@/layouts/AppLayout.vue';
import { home } from '@/routes';
import { notifications } from '@/routes/settings';

defineProps<{ title: string; message: string }>();
</script>

<template>
    <Head :title="title" />

    <AppLayout>
        <div class="mx-auto max-w-xl py-10 text-center">
            <h1 class="text-2xl font-semibold tracking-tight">{{ title }}</h1>
            <p class="mt-3 text-[#4a4a46] dark:text-[#c4c3be]">{{ message }}</p>
            <p class="mt-6 flex justify-center gap-4 text-sm">
                <Link
                    :href="notifications()"
                    class="underline underline-offset-4"
                    >Notification settings</Link
                >
                <Link :href="home()" class="underline underline-offset-4"
                    >Home</Link
                >
            </p>
        </div>
    </AppLayout>
</template>
