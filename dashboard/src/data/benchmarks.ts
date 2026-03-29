export const sendBenchmarks = [
  {
    name: "Send (Simple)",
    qorpe: 297.8,
    mediatr: 303.9,
  },
  {
    name: "Send (Complex)",
    qorpe: 316.5,
    mediatr: 337.2,
  },
  {
    name: "Send (with Pipeline)",
    qorpe: 1_120,
    mediatr: 1_200,
  },
];

export const publishBenchmarks = [
  {
    name: "Publish (1 Handler)",
    qorpe: 397.5,
    mediatr: 736.1,
  },
  {
    name: "Publish (3 Handlers)",
    qorpe: 523.8,
    mediatr: 1_073.2,
  },
  {
    name: "Publish (5 Handlers)",
    qorpe: 694.2,
    mediatr: 2_056.7,
  },
];

export const memoryBenchmarks = [
  {
    name: "Send",
    qorpe: 480,
    mediatr: 600,
  },
  {
    name: "Publish (1H)",
    qorpe: 376,
    mediatr: 1_264,
  },
  {
    name: "Publish (3H)",
    qorpe: 376,
    mediatr: 1_776,
  },
  {
    name: "Publish (5H)",
    qorpe: 376,
    mediatr: 2_288,
  },
];
