#include "stdafx.h"
#include "IVHACD.h"

#include <cstdlib>

namespace ConvexLibraryWrapper
{
	IVHACD::IVHACD(void)
	{
		instance = VHACD::CreateVHACD();
	}

	IVHACD::~IVHACD(void) {}

	void IVHACD::Cancel(void)
	{
		instance->Cancel();
	}

	bool IVHACD::Compute(array<float> ^ points,
						const unsigned int stridePoints,
						const unsigned int countPoints,
						array<int> ^ triangles,
						const unsigned int strideTriangles,
						const unsigned int countTriangles,
						Parameters ^ params)
	{
		VHACD::IVHACD::Parameters * newParams = new VHACD::IVHACD::Parameters();
		params->CopyToUnmanaged(newParams);

		float * meshPoints = (float *) malloc(points->Length * sizeof(float));
		System::Runtime::InteropServices::Marshal::Copy(points, 0, System::IntPtr((void *)meshPoints), points->Length);

		int * meshTriangles = (int *) malloc(triangles->Length * sizeof(unsigned int));
		System::Runtime::InteropServices::Marshal::Copy(triangles, 0, System::IntPtr((void *)meshTriangles), triangles->Length);

		return instance->Compute((const float * const) meshPoints, stridePoints, countPoints, 
			                     (const int * const) meshTriangles, strideTriangles, countTriangles, 
			                     (const VHACD::IVHACD::Parameters &) *newParams);
	}

	//bool IVHACD::Compute(double points[],
	//					 const unsigned int stridePoints,
	//					 const unsigned int countPoints,
	//					 int triangles[],
	//					 const unsigned int strideTriangles,
	//					 const unsigned int countTriangles,
	//					 Parameters ^ params)
	//{
	//	VHACD::IVHACD::Parameters * newParams = new VHACD::IVHACD::Parameters();
	//	params->CopyToUnmanaged(newParams);
	//	return instance->Compute(points, stridePoints, countPoints, triangles, strideTriangles, countTriangles,
	//		                     (const VHACD::IVHACD::Parameters &) newParams);
	//}

	unsigned int IVHACD::GetNConvexHulls(void)
	{
		return instance->GetNConvexHulls();
	}

	ConvexHull ^ IVHACD::GetConvexHull(const unsigned int index)
	{
		VHACD::IVHACD::ConvexHull * ch = new VHACD::IVHACD::ConvexHull();
		instance->GetConvexHull(index, (VHACD::IVHACD::ConvexHull &) *ch);
		ConvexHull ^ hull = gcnew ConvexHull(ch);
		return hull;
	}

	void IVHACD::Clean(void)
	{
		instance->Clean();
	}

	void IVHACD::Release(void)
	{
		instance->Release();
	}

}
