// Copyright Epic Games, Inc. All Rights Reserved.

#ifdef RL_BUILD_WITH_AVX

#include "rltests/Defs.h"
#include "rltests/joints/bpcm/Assertions.h"
#include "rltests/joints/bpcm/BPCMFixturesBlock8.h"

#include "riglogic/TypeDefs.h"
#include "riglogic/joints/JointsBuilder.h"
#include "riglogic/joints/bpcm/Evaluator.h"
#include "riglogic/joints/bpcm/strategies/AVX.h"
#include "riglogic/riglogic/RigLogic.h"

namespace {

template<typename TValue>
class AVXJointStorageBuilderTest : public ::testing::Test {
    protected:
        void buildStorage() {
            rl4::Configuration config{};
            config.calculationType = rl4::CalculationType::AVX;
            auto builder = rl4::JointsBuilder::create(config, &memRes);
            builder->computeStorageRequirements(&reader);
            builder->allocateStorage(&reader);
            builder->fillStorage(&reader);
            auto joints = builder->build();
            auto jointsImpl = static_cast<rl4::bpcm::Evaluator<TValue>*>(joints.get());

            auto strategy = pma::UniqueInstance<rl4::bpcm::AVXJointCalculationStrategy<TValue>,
                                                rl4::bpcm::JointCalculationStrategy<TValue> >::with(&memRes).create();
            auto expected = block8::OptimizedStorage<TValue>::create(std::move(strategy), &memRes);

            rl4::bpcm::Evaluator<TValue>::Accessor::assertRawDataEqual(*jointsImpl, expected);
            rl4::bpcm::Evaluator<TValue>::Accessor::assertJointGroupsEqual(*jointsImpl, expected);
            rl4::bpcm::Evaluator<TValue>::Accessor::assertLODsEqual(*jointsImpl, expected);
        }

    protected:
        pma::AlignedMemoryResource memRes;
        block8::CanonicalReader reader;

};

}  // namespace

using AVXStorageValueTypeList = ::testing::Types<StorageValueType>;
TYPED_TEST_SUITE(AVXJointStorageBuilderTest, AVXStorageValueTypeList, );

TYPED_TEST(AVXJointStorageBuilderTest, LayoutOptimization) {
    this->buildStorage();
}

#endif  // RL_BUILD_WITH_AVX