// 禁用测试并行化：PrintEncoder 是单例，多个测试类同时调用 PrintAsync 会产生状态竞争。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
