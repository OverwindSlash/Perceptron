# 算法模块实现流程
1. 算法核心执行类为 Executor，并继承自 AlgorithmBase
2. 在 Executor 的构造函数中给 AlgorithmName，AlgorithmVersion，AlgorithmDescription 赋值
3. override Initialize() 函数，在此函数中可以获得用于 DI 的 provider，以及使用 PreferenceParser 解析算法参数
4. 创建 Event 目录，并在其中创建事件类，事件类需要继承自 DomainEvent。并在事件类中定义 EventType，使用 EventType 调用基类构造函数。
5. 在事件类的构造函数中，给 Message 赋值
6. 在 Executor 中创建事件发布器：private IPublisher<XXXEvent> _xxxEventPublisher;
7. Analyze() 函数中，第一行使用 frame.Retain() 保留帧，最后一行使用 frame.Dispose() 释放帧