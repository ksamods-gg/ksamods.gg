<script setup lang="ts">
import { Link, usePage } from '@inertiajs/vue3';
import { computed, onMounted, ref } from 'vue';
import {
    consent,
    consentOpen,
    loadGoogleAnalytics,
    readConsent,
    saveConsent,
} from '@/lib/consent';
import { privacy } from '@/routes';

const page = usePage();
const google = computed(() => page.props.analytics.google);

// Client-side only: the server never knows the choice, so it never renders the banner.
const mounted = ref(false);
const choosing = ref(false);
const statistics = ref(false);

const visible = computed(
    () =>
        mounted.value &&
        google.value !== null &&
        (consent.value === null || consentOpen.value),
);

onMounted(() => {
    consent.value = readConsent();
    statistics.value = consent.value?.statistics ?? false;
    mounted.value = true;
    apply();
});

function apply(): void {
    if (google.value && consent.value?.statistics) {
        loadGoogleAnalytics(google.value);
    }
}

function choose(allowStatistics: boolean): void {
    saveConsent({ statistics: allowStatistics });
    statistics.value = allowStatistics;
    consentOpen.value = false;
    choosing.value = false;
    apply();
}

const button =
    'rounded-md border border-[#19140035] px-3 py-1.5 text-sm font-medium hover:border-[#1915014a] dark:border-[#3E3E3A] dark:hover:border-[#62605b]';
const muted = 'text-[#706f6c] dark:text-[#A1A09A]';
</script>

<template>
    <section
        v-if="visible"
        role="dialog"
        :aria-label="$t('Cookie settings')"
        class="fixed right-4 bottom-[calc(1rem+env(safe-area-inset-bottom,0px))] left-4 z-50 mx-auto max-w-xl rounded-lg border border-[#19140035] bg-white p-4 text-sm shadow-lg dark:border-[#3E3E3A] dark:bg-[#161615]"
    >
        <p>
            {{
                $t(
                    'We use cookies the site needs to work. With your permission, we also use Google Analytics to count visits, so we know what to improve.',
                )
            }}
            <Link :href="privacy()" class="underline">{{ $t('Privacy') }}</Link>
        </p>

        <div v-if="choosing" class="mt-3 space-y-3">
            <label class="flex items-start gap-2.5">
                <input type="checkbox" checked disabled class="mt-0.5" />
                <span>
                    {{ $t('Necessary') }}
                    <span :class="['block text-xs', muted]">{{
                        $t(
                            'Sign-in, security and your language choice. Always on.',
                        )
                    }}</span>
                </span>
            </label>
            <label class="flex cursor-pointer items-start gap-2.5">
                <input v-model="statistics" type="checkbox" class="mt-0.5" />
                <span>
                    {{ $t('Statistics') }}
                    <span :class="['block text-xs', muted]">{{
                        $t(
                            'Google Analytics counts visits and pages viewed. Google receives your IP address and browser details.',
                        )
                    }}</span>
                </span>
            </label>
        </div>

        <!-- Accept and reject weigh the same: neither choice is the easier one. -->
        <div class="mt-3 flex flex-wrap gap-2">
            <button type="button" :class="button" @click="choose(true)">
                {{ $t('Accept all') }}
            </button>
            <button type="button" :class="button" @click="choose(false)">
                {{ $t('Reject all') }}
            </button>
            <button
                v-if="choosing"
                type="button"
                :class="button"
                @click="choose(statistics)"
            >
                {{ $t('Save choice') }}
            </button>
            <button
                v-else
                type="button"
                :class="button"
                @click="choosing = true"
            >
                {{ $t('Choose') }}
            </button>
        </div>
    </section>
</template>
