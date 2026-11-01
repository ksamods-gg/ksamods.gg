<script setup lang="ts">
import { Head } from '@inertiajs/vue3';
import { computed, ref } from 'vue';
import AppLayout from '@/layouts/AppLayout.vue';
import { login as discordLogin } from '@/routes/discord';
import { login as githubLogin } from '@/routes/github';

const remember = ref(false);
const query = computed(() =>
    remember.value ? { query: { remember: 1 } } : undefined,
);

const button =
    'block rounded-md border border-[#19140035] px-4 py-2.5 text-center text-sm font-medium hover:border-[#1915014a] dark:border-[#3E3E3A] dark:hover:border-[#62605b]';
const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
</script>

<template>
    <Head :title="$t('Log in')" />

    <AppLayout>
        <div class="mx-auto max-w-sm py-8">
            <h1 class="text-2xl font-semibold tracking-tight">
                {{ $t('Log in') }}
            </h1>
            <p :class="['mt-1 mb-6 text-sm', muted]">
                {{
                    $t('Use either account. You can link the other one later.')
                }}
            </p>

            <!-- Plain links: both leave the site for the provider's sign-in. -->
            <div class="flex flex-col gap-3">
                <a :href="githubLogin(query).url" :class="button">
                    {{ $t('Continue with GitHub') }}
                </a>
                <a :href="discordLogin(query).url" :class="button">
                    {{ $t('Continue with Discord') }}
                </a>
            </div>

            <!-- Off unless chosen: a sign-in that outlives the session is a choice, not a default. -->
            <label class="mt-4 flex cursor-pointer items-start gap-2.5 text-sm">
                <input v-model="remember" type="checkbox" class="mt-0.5" />
                <span>
                    {{ $t('Keep me signed in') }}
                    <span :class="['block text-xs', muted]">{{
                        $t(
                            'Stay signed in on this browser for weeks. Leave it off on a shared computer.',
                        )
                    }}</span>
                </span>
            </label>

            <p :class="['mt-6 text-xs', muted]">
                {{
                    $t(
                        'Submitting and editing mod listings opens pull requests on GitHub, so it needs a linked GitHub account.',
                    )
                }}
            </p>
        </div>
    </AppLayout>
</template>
