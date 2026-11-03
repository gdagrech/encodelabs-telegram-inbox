import { defineConfig } from 'vitest/config';

/**
 * Merged into the configuration that Angular's unit-test builder generates.
 *
 * The only departure from the defaults is the worker pool. Vitest forks child processes by default,
 * which never start inside the container used to run these tests —they time out with
 * "Timeout starting forks runner"— so the suite runs on worker threads instead. Threads are also
 * faster here, because every spec shares one Angular compilation.
 */
export default defineConfig({
  test: {
    pool: 'threads',
    // One worker, no parallelism between spec files. The suite is small enough that parallelism buys
    // nothing, and starting several workers at once times out on a machine that is already running
    // the Compose stack — which is exactly the situation the verification script runs in.
    fileParallelism: false,
    maxWorkers: 1,
    minWorkers: 1,
  },
});
